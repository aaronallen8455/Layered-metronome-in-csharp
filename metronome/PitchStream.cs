using System;
using System.Collections.Generic;
using System.Linq;
using NAudio.Wave;

namespace Pronome
{
    /**<summary>An audio stream for pitch 'beeps'.</summary>*/
    public class PitchStream : ISampleProvider, IStreamProvider
    {
        /**<summary>The WaveFormat object for this stream.</summary>*/
        private readonly WaveFormat waveFormat;

        /**<summary>The BeatCollection object, contains enumerator for byte interval values.</summary>*/
        public SourceBeatCollection BeatCollection { get; set; }

        /**<summary>Test for whether this is a pitch source.</summary>*/
        public bool IsPitch { get { return true; } }

        // Const Math
        private const double TwoPi = 2 * Math.PI;

        /**<summary>The number of bytes/Second for this audio stream.</summary>*/
        public int BytesPerSec { get; set; }

        /**<summary>The layer that this audiosource is used in.</summary>*/
        public Layer Layer { get; set; }

        /**<summary>Used in sine wave generation.</summary>*/
        private float nSample;

        /**<summary>Constructor</summary>
         * <param name="channel">Number of channels</param>
         * <param name="sampleRate">Samples per second</param>
         */
        public PitchStream(int sampleRate = 16000, int channel = 2)
        {
            waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channel);
            // Default
            Frequency = BaseFrequency = 440.0;
            Pan = 0;
            BytesPerSec = waveFormat.AverageBytesPerSecond / 8;
            freqEnum = Frequencies.Values.GetEnumerator();

            // set audible/silent interval if already exists
            if (Metronome.GetInstance().IsSilentInterval)
                SetSilentInterval(Metronome.GetInstance().AudibleInterval, Metronome.GetInstance().SilentInterval);
        }

        /**<summary>Add a frequency to the frequency enumerator que.</summary>
         * <param name="cell">The cell that uses the pitch.</param>
         * <param name="symbol">The pitch symbol. ex. A4</param>
         */
        public void SetFrequency(string symbol, BeatCell cell)
        {
            Frequencies.Add(cell, ConvertFromSymbol(symbol));
            freqEnum = Frequencies.Values.GetEnumerator();
        }

        /**<summary>Convert a pitch symbol or raw number into a hertz value.</summary>
         * <param name="symbol">The symbol to convert from.</param>
         */
        public static double ConvertFromSymbol(string symbol)
        {
            string note = new string(symbol.TakeWhile((x) => !char.IsNumber(x)).ToArray()).ToLower();
            if (note == string.Empty) // raw pitch value
            {
                return Convert.ToDouble(symbol);
            }
            string o = new string(symbol.SkipWhile((x) => !char.IsNumber(x)).ToArray());
            int octave;
            if (o != string.Empty) octave = Convert.ToInt32(o) - 5;
            else octave = 4;

            float index = Notes[note];
            index += octave * 12;
            double frequency = 440 * Math.Pow(2, index / 12);
            return frequency;
        }

        /**<summary>Used in converting symbols to pitches.</summary>*/
        protected static Dictionary<string, int> Notes = new Dictionary<string, int>
        {
            { "a", 12 }, { "a#", 13 }, { "bb", 13 }, { "b", 14 }, { "c", 3 },
            { "c#", 4 }, { "db", 4 }, { "d", 5 }, { "d#", 6 }, { "eb", 6 },
            { "e", 7 }, { "f", 8 }, { "f#", 9 }, { "gb", 9 }, { "g", 10 },
            { "g#", 11 }, { "ab", 11 }
        };

        /**<summary>The format of this stream.</summary>*/
        public WaveFormat WaveFormat
        {
            get { return waveFormat; }
        }

        /**<summary>A dictionary of frequencies and the cells they are tied to.</summary>*/
        public Dictionary<BeatCell, double> Frequencies = new Dictionary<BeatCell, double>();
        /**<summary>Used to cycle through the pitch frequencies used by this source.</summary>*/
        protected IEnumerator<double> freqEnum;

        /**<summary>Reset state to default values.</summary>*/
        public void Reset()
        {
            freqEnum.Reset(); //= Frequencies.Values.GetEnumerator();
            BeatCollection.Enumerator = BeatCollection.GetEnumerator();
            ByteInterval = 0;
            previousSample = 0;
            Gain = Volume;
            if (Metronome.GetInstance().IsSilentInterval)
            {
                SetSilentInterval(Metronome.GetInstance().AudibleInterval, Metronome.GetInstance().SilentInterval);
            }
            if (Metronome.GetInstance().IsRandomMute)
            {
                randomMuteCountdown = null;
                currentlyMuted = false;
            }
            if (Layer.Offset > 0)
            {
                SetOffset(
                    BeatCell.ConvertFromBpm(Layer.Offset, this)
                );
            }
        }

        /**<summary>Get the next frequency in the sequence.</summary>*/
        public double GetNextFrequency()
        {
            if (freqEnum.MoveNext()) return freqEnum.Current;
            else
            {
                freqEnum.Reset();
                return GetNextFrequency();
                //freqEnum.MoveNext();
                //return freqEnum.Current;
            }
        }

        /**<summary>The current frequency in hertz.</summary>*/
        public double Frequency { get; set; }

        /**<summary>The frequency used if cell doesn't specify a pitch directly.</summary>*/
        public double BaseFrequency { get; set; }

        /**<summary>Used to create the fade out of the beep sound. Resets to the value of Volume on interval completetion.</summary>*/
        protected double Gain { get; set; }
        double gainStep = .0003; // the amount that gain is subtracted by for each byte to produce fade.
        double newGainStep; // set when Volume changes and takes effect when byte interval resets.

        /**<summary>If a multiply is cued, perform operation on all relevant members at the start of a stream read.</summary>*/
        public void MultiplyByteInterval()
        {
            lock (_multLock)
            {
                if (intervalMultiplyCued)
                {
                    BeatCollection.MultiplyBeatValues();

                    double mult = intervalMultiplyFactor * ByteInterval;
                    ByteInterval = (int)mult;
                    Layer.Remainder *= intervalMultiplyFactor;
                    Layer.Remainder += mult - ByteInterval;

                    if (Layer.Remainder >= 1)
                    {
                        ByteInterval += (int)Layer.Remainder;
                        Layer.Remainder -= (int)Layer.Remainder;
                    }

                    // multiply the offset aswell
                    if (hasOffset)
                    {
                        mult = intervalMultiplyFactor * InitialOffset;
                        InitialOffset = (int)mult;
                        OffsetRemainder *= intervalMultiplyFactor;
                        OffsetRemainder += mult - InitialOffset;
                    }

                    intervalMultiplyCued = false;
                }
            }
        }
        /**<summary>Cue a multiply operation to occur at the start of the next stream read.</summary>
         * <param name="factor">The number to multiply by</param>
         */
        public void MultiplyByteInterval(double factor)
        {
            lock(_multLock)
            {
                if (!intervalMultiplyCued)
                {
                    intervalMultiplyFactor = factor;
                    intervalMultiplyCued = true;
                }
            }
        }
        bool intervalMultiplyCued = false;
        double intervalMultiplyFactor;
        object _multLock = new object();

        /**<summary>The volume control for this stream.</summary>*/
        public double Volume
        {
            get { return _volume; }
            set
            {
                double ratio = value / _volume;
                _volume = value;
                newGainStep = value * .0003;
            }
        }
        double _volume = 1;

        private volatile float pan;
        /**<summary>Gets/sets the pan value. -1 to 1.</summary>*/
        public float Pan
        {
            get { return pan; }
            set
            {
                pan = value;

                left = (Pan + 1f) / 2;
                right = (2 - (Pan + 1f)) / 2;
            }
        }
        private float left;
        private float right;

        /**<summary>Gets the next interval value and determines if it will be muted.</summary>*/
        public int GetNextInterval()
        {
            BeatCollection.Enumerator.MoveNext();
            int result = BeatCollection.Enumerator.Current;
            // hand silent interval

            if (IsSilentIntervalSilent())
            {
                previousByteInterval = result;
                return result;
            }
            
            currentlyMuted = IsRandomMuted();

            previousByteInterval = result;

            return result;
        }

        protected double SilentInterval; // total samples in silent interval
        protected double AudibleInterval; // total samples in audible interval
        protected int currentSlntIntvl; // samples in current interval (silent or audible)
        protected bool silentIntvlSilent = false; // currently silent
        protected double SilentIntervalRemainder; // fractional portion

        /**<summary>Sets the silent interval.</summary>
         * <param name="audible">Number of quarter notes audible.</param>
         * <param name="silent">Number of quarter notes silent.</param>
         */
        public void SetSilentInterval(double audible, double silent)
        {
            AudibleInterval = BeatCell.ConvertFromBpm(audible, this);
            SilentInterval = BeatCell.ConvertFromBpm(silent, this);
            currentSlntIntvl = (int)AudibleInterval - InitialOffset;
            SilentIntervalRemainder = audible - currentSlntIntvl + OffsetRemainder;
        }

        protected int? randomMuteCountdown = null; // If the rand mute has a countdown, we track it here
        protected int randomMuteCountdownTotal; // The rand mute initial countdown value.
        protected bool currentlyMuted = false; // true if sound is randomly muted.

        /**<summary>Returns true if the note should be randomly muted.</summary>*/
        protected bool IsRandomMuted()
        {
            if (!Metronome.GetInstance().IsRandomMute)
            {
                currentlyMuted = false;
                return false;
            }

            // init countdown
            if (randomMuteCountdown == null && Metronome.GetInstance().RandomMuteSeconds > 0)
            {
                randomMuteCountdown = randomMuteCountdownTotal = Metronome.GetInstance().RandomMuteSeconds * BytesPerSec - InitialOffset;
            }

            int rand = Metronome.GetRandomNum();

            if (randomMuteCountdown == null)
                return rand < Metronome.GetInstance().RandomMutePercent;
            else
            {
                // countdown
                if (randomMuteCountdown > 0) randomMuteCountdown -= previousByteInterval;
                else if (randomMuteCountdown < 0) randomMuteCountdown = 0;

                float factor = (float)(randomMuteCountdownTotal - randomMuteCountdown) / randomMuteCountdownTotal;
                return rand < Metronome.GetInstance().RandomMutePercent * factor;
            }
        }

        /**<summary>Returns true if silent interval is currently silent.</summary>*/
        protected bool IsSilentIntervalSilent() // check if silent interval is currently silent or audible. Perform timing shifts
        {
            if (!Metronome.GetInstance().IsSilentInterval) return false;

            currentSlntIntvl -= previousByteInterval;
            if (currentSlntIntvl <= 0)
            {
                do
                {
                    silentIntvlSilent = !silentIntvlSilent;
                    double nextInterval = silentIntvlSilent ? SilentInterval : AudibleInterval;
                    currentSlntIntvl += (int)nextInterval;
                    SilentIntervalRemainder += nextInterval - ((int)nextInterval);
                    if (SilentIntervalRemainder >= 1)
                    {
                        currentSlntIntvl++;
                        SilentIntervalRemainder--;
                    }
                } while (currentSlntIntvl < 0);
            }

            return silentIntvlSilent;
        }

        /**<summary>Set the amount of offset in samples.</summary>
         * <param name="value">Value in samples.</param>
         */
        public void SetOffset(double value)
        {
            InitialOffset = (int)(value / 2);
            OffsetRemainder = value - InitialOffset;
            hasOffset = true;
        }

        /**<summary>Get the current amount of offset in samples.</summary>*/
        public double GetOffset()
        {
            return InitialOffset + OffsetRemainder;
        }

        protected int InitialOffset = 0; // time to wait before reading source.
        protected double OffsetRemainder = 0;
        protected bool hasOffset = false;
        protected bool lastIntervalMuted = false; // used to cycle pitch if the last interval was randomly muted.

        protected int previousByteInterval;
        protected int ByteInterval;

        double multiple; // used in sine wave calculation

        double previousSample;

        /**<summary>Reads from the audio stream.</summary>
         * <param name="buffer">Sample array buffer.</param>
         */
        public int Read(float[] buffer, int offset, int count)
        {
            int outIndex = offset;

            // perform cued interval multiplication
            if (intervalMultiplyCued)
                MultiplyByteInterval();

            double sampleValue;

            // Complete Buffer
            for (int sampleCount = 0; sampleCount < count / waveFormat.Channels; sampleCount++)
            {
                // account for offset
                if (hasOffset)
                {
                    InitialOffset -= 1;

                    buffer[outIndex++] = 0;
                    buffer[outIndex++] = 0;

                    if (InitialOffset == 0)
                    {
                        hasOffset = false;
                        Layer.Remainder += OffsetRemainder;
                    }
                    // add remainder to layer.R
                    continue;
                }
                bool freqChanged = false;
                // interval is over, reset
                if (ByteInterval == 0)
                {
                    double curFreq = Frequency;
                    Frequency = GetNextFrequency();
                    ByteInterval = GetNextInterval();
                    if (!silentIntvlSilent && !currentlyMuted && Frequency != 0)
                    {
                        // what should nsample be to create a smooth transition?
                        if (previousSample != 0 && Gain != 0)
                        {
                            if (Frequency != curFreq)
                                multiple = TwoPi * Frequency / waveFormat.SampleRate;
                            nSample = Convert.ToSingle(Math.Asin(previousSample / Volume) / multiple);
                            nSample += .5f; // seems to help
                        }
                        else nSample = 0;
                        freqChanged = true;
                        Gain = Volume;
                        if (gainStep != newGainStep) // set new gainstep if volume was changed
                            gainStep = newGainStep;
                    }
                    else Frequency = curFreq; //retain frequency if random/interval muting occurs.
                }

                if (Gain <= 0)
                {
                    nSample = 0;
                    sampleValue = 0;
                }
                else
                {
                    // check for muting
                    if (Layer.IsMuted || Layer.SoloGroupEngaged && !Layer.IsSoloed)
                    {
                        nSample = 0;
                        previousSample = sampleValue = 0;
                    }
                    else
                    {
                        // Sin Generator
                        if (freqChanged)
                        {
                            multiple = TwoPi * Frequency / waveFormat.SampleRate; // reuse this value
                            freqChanged = false;
                        }
                        sampleValue = previousSample = Gain * Math.Sin(nSample * multiple);
                    }
                    Gain -= gainStep;
                }
                nSample++;

                // Set the pan amounts.
                for (int i = 0; i < waveFormat.Channels; i++)
                {
                    if (i == 0)
                        buffer[outIndex++] = (float)sampleValue * right;
                    else
                        buffer[outIndex++] = (float)sampleValue * left;
                }
                ByteInterval -= 1;
            }

            return count;
        }

        public void Dispose() { }
    }
}
