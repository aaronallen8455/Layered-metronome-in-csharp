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

        // Generator variable
        private float nSample;

        public PitchStream(int sampleRate = 16000, int channel = 2)
        {
            waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channel);

            // Default
            Frequency = BaseFrequency = 440.0;
            Volume = .6f;
            //Gain = .6f;
            Pan = 0;
            BytesPerSec = waveFormat.AverageBytesPerSecond / 8;
            freqEnum = Frequencies.Values.GetEnumerator();

            // set audible/silent interval if already exists
            if (Metronome.GetInstance().IsSilentInterval)
                SetSilentInterval(Metronome.GetInstance().AudibleInterval, Metronome.GetInstance().SilentInterval);
        }

        public void SetFrequency(string symbol, BeatCell cell)
        {
            Frequencies.Add(cell, ConvertFromSymbol(symbol));
            freqEnum = Frequencies.Values.GetEnumerator();
        }

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

        protected static Dictionary<string, int> Notes = new Dictionary<string, int>
        {
            { "a", 12 }, { "a#", 13 }, { "bb", 13 }, { "b", 14 }, { "c", 3 },
            { "c#", 4 }, { "db", 4 }, { "d", 5 }, { "d#", 6 }, { "eb", 6 },
            { "e", 7 }, { "f", 8 }, { "f#", 9 }, { "gb", 9 }, { "g", 10 },
            { "g#", 11 }, { "ab", 11 }
        };

        //public WaveStream Channel { get; set; }

        public WaveFormat WaveFormat
        {
            get { return waveFormat; }
        }

        // dictionary of frequencies and the cells they are tied to.
        //public List<KeyValuePair<BeatCell, double>> Frequencies = new List<KeyValuePair<BeatCell, double>>();
        public Dictionary<BeatCell, double> Frequencies = new Dictionary<BeatCell, double>();
        protected IEnumerator<double> freqEnum;

        public void Reset()
        {
            freqEnum = Frequencies.Values.GetEnumerator();
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

        // get the next frequency in the sequence
        public double GetNextFrequency()
        {
            if (freqEnum.MoveNext()) return freqEnum.Current;
            else
            {
                freqEnum = Frequencies.Values.GetEnumerator();
                freqEnum.MoveNext();
                return freqEnum.Current;
            }
        }

        // the current frequency
        public double Frequency { get; set; }

        // the base frequency. used if cell doesn't specify a pitch
        public double BaseFrequency { get; set; }

        public double Gain { get; set; }

        public void MultiplyByteInterval()
        {
            if (intervalMultiplyCued)
            {
                BeatCollection.MultiplyBeatValues();

                double mult = intervalMultiplyFactor * ByteInterval;
                ByteInterval = (int)mult;
                Layer.Remainder += mult - ByteInterval;

                // multiply the offset aswell
                if (hasOffset)
                {
                    mult = intervalMultiplyFactor * InitialOffset;
                    InitialOffset = (int)mult;
                    OffsetRemainder += mult - InitialOffset;
                }

                intervalMultiplyCued = false;
            }
        }
        public void MultiplyByteInterval(double factor)
        {
            if (!intervalMultiplyCued)
            {
                intervalMultiplyFactor = factor;
                intervalMultiplyCued = true;
            }
        }
        bool intervalMultiplyCued = false;
        double intervalMultiplyFactor;

        public double Volume
        {
            get; set;
        }

        private volatile float pan;
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

        public void SetSilentInterval(double audible, double silent)
        {
            AudibleInterval = BeatCell.ConvertFromBpm(audible, this);
            SilentInterval = BeatCell.ConvertFromBpm(silent, this);
            currentSlntIntvl = (int)AudibleInterval - InitialOffset;
            SilentIntervalRemainder = audible - currentSlntIntvl + OffsetRemainder;
        }

        protected int? randomMuteCountdown = null;
        protected int randomMuteCountdownTotal;
        protected bool currentlyMuted = false;

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

        public void SetOffset(double value)
        {
            InitialOffset = (int)(value / 2);
            OffsetRemainder = value - InitialOffset;
            hasOffset = true;
        }

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

        double previousSample;

        public int Read(float[] buffer, int offset, int count)
        {
            int outIndex = offset;

            // perform cued interval multiplication
            if (intervalMultiplyCued)
                MultiplyByteInterval();

            // Generator current value
            double multiple;
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
                            multiple = TwoPi * Frequency / waveFormat.SampleRate;
                            nSample = Convert.ToSingle(Math.Asin(previousSample / Volume) / multiple);
                            nSample += .5f; // seems to help
                        }
                        else nSample = 0;

                        Gain = Volume;
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
                        multiple = TwoPi * Frequency / waveFormat.SampleRate;
                        sampleValue = previousSample = Gain * Math.Sin(nSample * multiple);
                    }
                    Gain -= .0003; //.0002 for .6 .0003 for 1
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
