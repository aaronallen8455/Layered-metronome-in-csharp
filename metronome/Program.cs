﻿using System;
using System.Collections.Generic;
using System.Collections;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Media;
using System.Threading;
using System.IO;
using System.Diagnostics;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;


namespace Metronome
{
    class Program
    {
        static void Main(string[] args)
        {
            Metronome metronome = Metronome.GetInstance();
            metronome.Tempo = 60f;

            var layer1 = new Layer(new BeatCell[]
            {
                new BeatCell(2d)
            }, "hihat_pedal_v5.wav");
            layer1.Volume = .6f;
            layer1.Pan = 1;
            layer1.SetOffset(1d);
            
            var layer2 = new Layer(new BeatCell[]
            {
                new BeatCell(4/5d), new BeatCell(4/5d, "d4"), new BeatCell(4/5d, "e4")
            }, "c#4");

            var layer3 = new Layer(new BeatCell[]
            {
                new BeatCell(2/3d)//, new BeatCell(2/3f), new BeatCell(1/3f)
            }, "snare_xstick_v16.wav");
            layer3.SetOffset(1 / 3d);

            var layer4 = new Layer(new BeatCell[]
            {
                new BeatCell(1d), new BeatCell(2/3d), new BeatCell(1/3d)
            }, "ride_center_v8.wav");
            
            //var layer4 = new Layer(new BeatCell[]
            //{
            //    new BeatCell(4/11f)
            //}, "G5");
            //
            //var layer5 = new Layer(new BeatCell[]
            //{
            //    new BeatCell(4/17f)
            //}, "C5");
            //layer5.Volume = .1f;
            //layer5.Pan = -1;

            metronome.AddLayer(layer1);
            metronome.AddLayer(layer2);
            metronome.AddLayer(layer3);
            metronome.AddLayer(layer4);
            //metronome.AddLayer(layer5);
            metronome.SetRandomMute(10);
            
            Thread.Sleep(2000);

            metronome.Start();
            Console.ReadKey();
            metronome.Stop();
            Console.ReadKey();
            metronome.Dispose();
        }
    }


    public class Metronome : IDisposable
    {
        protected MixingSampleProvider Mixer = new MixingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(16000, 2));
        protected DirectSoundOut Player = new DirectSoundOut();

        static Metronome Instance;

        protected int LayerIndex = 0;
        protected List<Layer> Layers = new List<Layer>();

        private Metronome()
        {
            Player.Init(Mixer);
        }

        static public Metronome GetInstance()
        {
            if (Instance == null)
                Instance = new Metronome();
            return Instance;
        }

        public void AddLayer(Layer layer)
        {
            Layers.Add(layer);
            // add sources to mixer
            foreach (IStreamProvider src in layer.AudioSources.Values)
            {
                if (src.IsPitch)
                {
                    Mixer.AddMixerInput((ISampleProvider)src);
                }else
                {
                    Mixer.AddMixerInput(((WavFileStream)src).Channel);
                }
            }

            if (layer.IsPitch) // if base source is a pitch stream.
                Mixer.AddMixerInput((ISampleProvider)layer.BasePitchSource);
            else
            {
                Mixer.AddMixerInput(((WavFileStream)layer.BaseAudioSource).Channel);
                // check if has a pitch stream
                if (layer.BasePitchSource != default(PitchStream))
                    Mixer.AddMixerInput(layer.BasePitchSource);
            }
        }

        public void Start()
        {
            Player.Play();
        }

        public void Stop()
        {
            Player.Stop();
        }

        public float Tempo // in BPM
        {
            get; set;
        }

        public static Random Rand = new Random();

        public bool IsRandomMute = false;

        public int RandomMutePercent;

        public void SetRandomMute(int percent)
        {
            RandomMutePercent = percent < 100 && percent >= 0 ? percent : 0;
            IsRandomMute = RandomMutePercent > 0 ? true : false;
        }

        public void Dispose()
        {
            Player.Dispose();
            Layers.ForEach((x) => x.Dispose());
        }
    }


    public class Layer : IDisposable
    {
        protected List<BeatCell> Beat = new List<BeatCell>();

        public Dictionary<string, IStreamProvider> AudioSources = new Dictionary<string, IStreamProvider>();
        public IStreamProvider BaseAudioSource;
        public PitchStream BasePitchSource; // only use one pitch source per layer
        public bool IsPitch;

        public double Remainder = .0; // holds the accumulating fractional milliseconds.
        public float Offset = 0; // in BPM
        protected string BaseSourceName;

        public Layer(BeatCell[] beat, string baseSourceName)
        {
            SetBaseSource(baseSourceName);
            SetBeat(beat);
            Volume = .6f;
        }

        public void SetBaseSource(string baseSourceName)
        {
            BaseSourceName = baseSourceName;
            // is sample or pitch source?
            if (baseSourceName.Count() <= 5)
            {
                BasePitchSource = new PitchStream();
                //BasePitchSource.SetFrequency(baseSourceName);
                BasePitchSource.BaseFrequency = PitchStream.ConvertFromSymbol(baseSourceName);
                BasePitchSource.Layer = this;
                BaseAudioSource = BasePitchSource; // needs to be cast back to ISampleProvider when added to mixer
                IsPitch = true;
            }
            else
            {
                BaseAudioSource = new WavFileStream(baseSourceName);
                BaseAudioSource.Layer = this;
                IsPitch = false;
            }
            //BasePitchSource.Layer = this;
        }

        public void SetOffset(double offset)
        {
            foreach (IStreamProvider src in AudioSources.Values)
            {
                double current = src.GetOffset();
                double add = BeatCell.ConvertFromBpm(offset, src);
                src.SetOffset(current + add);
            }

            // set for pitch / base source
            double current2 = BaseAudioSource.GetOffset();
            double add2 = BeatCell.ConvertFromBpm(offset, BaseAudioSource);
            BaseAudioSource.SetOffset(current2 + add2);

            if (BasePitchSource != default(PitchStream))
            {
                double current3 = BasePitchSource.GetOffset();
                double add3 = BeatCell.ConvertFromBpm(offset, BasePitchSource);
                BasePitchSource.SetOffset(current3 + add3);
            }
        }

        public void SetBeat(BeatCell[] beat)
        {
            //float tempo = Metronome.GetInstance().Tempo;
            List<string> sources = new List<string>();

            for (int i = 0; i < beat.Count(); i++)
            {
                beat[i].Layer = this;
                if (beat[i].SourceName != string.Empty && beat[i].SourceName.Count() > 5)
                {
                    var wavStream = new WavFileStream(beat[i].SourceName);
                    wavStream.Layer = this;
                    beat[i].AudioSource = wavStream;
                    AudioSources.Add(beat[i].SourceName, wavStream);
                }
                else
                {
                    if (beat[i].SourceName != string.Empty && beat[i].SourceName.Count() <= 5)
                    {
                        // beat has a defined pitch
                        // check if basepitch source exists
                        if (BasePitchSource == default(PitchStream))
                        {
                            BasePitchSource = new PitchStream();
                            //BasePitchSource.SetFrequency(beat[i].SourceName);
                            BasePitchSource.SetFrequency(beat[i].SourceName, beat[i]);
                            BasePitchSource.BaseFrequency = PitchStream.ConvertFromSymbol(beat[i].SourceName); // make the base freq
                            BasePitchSource.Layer = this;
                        }
                        beat[i].AudioSource = BasePitchSource;
                        BasePitchSource.SetFrequency(beat[i].SourceName, beat[i]);
                    }
                    else
                    {
                        if (IsPitch)
                        {
                            // no pitch defined, use base pitch
                            BasePitchSource.SetFrequency(BasePitchSource.BaseFrequency.ToString(), beat[i]);
                        }
                        beat[i].AudioSource = BaseAudioSource;
                    }
                }
                // set beat's value based on tempo and bytes/sec
                beat[i].SetBeatValue();
            }

            Beat = beat.ToList();

            SetBeatCollectionOnSources();
        }

        public void SetBeatCollectionOnSources()
        {
            List<IStreamProvider> completed = new List<IStreamProvider>();

            // for each beat, iterate over all beats and build a beat list of values from beats of same source.
            for (int i=0; i<Beat.Count; i++)
            {
                List<double> cells = new List<double>(); 
                double accumulator = 0;
                // Once per audio source
                if (completed.Contains(Beat[i].AudioSource)) continue;
                // if selected beat is not first in cycle, set it's offset
                if (i != 0)
                {
                    double offsetAccumulate = 0f;
                    for (int p=0; p<i; p++)
                    {
                        offsetAccumulate += Beat[p].Bpm;
                    }
                    Beat[i].AudioSource.SetOffset(BeatCell.ConvertFromBpm(offsetAccumulate, Beat[i].AudioSource));
                }
                // iterate over beats starting with current one
                for (int p=i; ; p++)
                {

                    if (p == Beat.Count) p = 0;

                    if (Beat[p].AudioSource == Beat[i].AudioSource)
                    {

                        // add accumulator to previous element in list
                        if (cells.Count != 0)
                        {
                            cells[cells.Count - 1] += accumulator;//BeatCell.ConvertFromBpm(accumulator, Beat[i].AudioSource);
                            accumulator = 0f;
                        }
                        cells.Add(Beat[p].Bpm);
                    }
                    else accumulator += Beat[p].Bpm;

                    // job done if current beat is one before the outer beat.
                    if (p == i - 1 || (i == 0 && p == Beat.Count - 1))
                    {
                        cells[cells.Count - 1] += accumulator;//BeatCell.ConvertFromBpm(accumulator, Beat[i].AudioSource);
                        break;
                    }
                }
                completed.Add(Beat[i].AudioSource);
                
                Beat[i].AudioSource.BeatCollection = new SourceBeatCollection(this, cells.ToArray(), Beat[i].AudioSource);
            }
        }

        protected float volume;
        public float Volume
        {
            get { return volume; }
            set
            {
                foreach (IStreamProvider src in AudioSources.Values) src.Volume = value;
                if (IsPitch) BasePitchSource.Volume = value;
                else
                {
                    // is there a pitch source?
                    if (BasePitchSource != default(PitchStream))
                        BasePitchSource.Volume = value;
                    BaseAudioSource.Volume = value;
                }
            }
        }

        public float Pan
        {
            set
            {
                foreach (IStreamProvider src in AudioSources.Values) src.Pan = value;
                if (IsPitch) BasePitchSource.Pan = value;
                else
                {
                    if (BasePitchSource != default(PitchStream))
                        BasePitchSource.Pan = value;
                    BaseAudioSource.Pan = value;
                }
            }
        }

        public void Dispose()
        {
            foreach(IStreamProvider src in AudioSources.Values)
            {
                src.Dispose();
            }

            BaseAudioSource.Dispose();
            if (IsPitch)
                BasePitchSource.Dispose();
        }
    }
    

    public class BeatCell
    {
        public double ByteInterval;
        public double Bpm; // value expressed in BPM time.
        public string SourceName;
        public Layer Layer;
        public IStreamProvider AudioSource;

        public void SetBeatValue()
        {
            // set byte interval based on tempo and audiosource sample rate
            ByteInterval = Bpm * (60 / Metronome.GetInstance().Tempo) * AudioSource.BytesPerSec;
        }

        static public double ConvertFromBpm(double bpm, IStreamProvider src)
        {
            double result = bpm * (60d / Metronome.GetInstance().Tempo) * src.WaveFormat.SampleRate;
            return result;
        }

        public BeatCell(double beat, string sourceName = "") // value of beat, ex. "1/3"
        {
            SourceName = sourceName;
            Bpm = beat;
        }
    }


    public class PitchStream : ISampleProvider, IStreamProvider
    {
        // Wave format
        private readonly WaveFormat waveFormat;

        public SourceBeatCollection BeatCollection { get; set; }

        public bool IsPitch { get { return true; } }

        // Const Math
        private const double TwoPi = 2 * Math.PI;

        public int BytesPerSec { get; set; }

        public Layer Layer { get; set; }

        // Generator variable
        private int nSample;
        
        public PitchStream(int sampleRate = 16000, int channel = 2)
        {
            waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channel);

            // Default
            Frequency = BaseFrequency = 440.0;
            Volume = .6f;
            Pan = 0;
            BytesPerSec = waveFormat.AverageBytesPerSecond / 8;
            freqEnum = Frequencies.Values.GetEnumerator();
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
        public Dictionary<BeatCell, double> Frequencies = new Dictionary<BeatCell, double>();
        protected IEnumerator<double> freqEnum;

        // get the next frequency in the sequence
        public double GetNextFrequency()
        {
            var e = Frequencies.Values.GetEnumerator();
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

        double volume;
        public double Volume {
            get
            {
                return volume;
            }

            set
            {
                volume = Gain = value;
            }
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
            // handle random mute
            if (Metronome.GetInstance().IsRandomMute)
            {
                int rand = (int)(Metronome.Rand.NextDouble() * 100);
                if (rand < Metronome.GetInstance().RandomMutePercent)
                {
                    BeatCollection.Enumerator.MoveNext();
                    result += BeatCollection.Enumerator.Current;
                    lastIntervalRandMuted = true;
                }
            }
            return result;
        }

        public void SetOffset(double value)
        {
            InitialOffset = (int)(value/2);
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
        protected bool lastIntervalRandMuted = false; // used to cycle pitch if the last interval was randomly muted.

        protected int ByteInterval;

        public int Read(float[] buffer, int offset, int count)
        {
            int outIndex = offset;
            // Generator current value
            double multiple;
            double sampleValue;
            // Complete Buffer
            for (int sampleCount = 0; sampleCount < count / waveFormat.Channels; sampleCount++)
            {
                // account for offset
                if (hasOffset)
                {
                    //for (int i=0; i<waveFormat.Channels; i++)
                    //{
                    //    buffer[outIndex++] = 0;
                    //}
                    InitialOffset -= 1;
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
                    if (lastIntervalRandMuted)
                    {
                        GetNextFrequency();
                        lastIntervalRandMuted = false;
                    }
                    Gain = Volume;
                    nSample = 0;
                    Frequency = GetNextFrequency();
                    ByteInterval = GetNextInterval();
                }
                if (Gain <= 0)
                {
                    nSample = 0;
                    sampleValue = 0;
                }
                else
                {
                    // Sin Generator
                    multiple = TwoPi * Frequency / waveFormat.SampleRate;
                    sampleValue = Gain * Math.Sin(nSample * multiple);
                    Gain -= .0002;
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


    public class WavFileStream : WaveStream, IStreamProvider
    {
        WaveFileReader sourceStream;

        public WaveChannel32 Channel { get; set; }

        public bool IsPitch { get { return false; } }

        public Layer Layer { get; set; }

        public SourceBeatCollection BeatCollection { get; set; }

        public int BytesPerSec { get; set; }

        byte[] cache;
        int cacheIndex = 0;

        public WavFileStream(string fileName)
        {
            sourceStream = new WaveFileReader(fileName);
            Channel = new WaveChannel32(this);
            BytesPerSec = sourceStream.WaveFormat.AverageBytesPerSecond;

            MemoryStream ms = new MemoryStream();
            sourceStream.CopyTo(ms);
            cache = ms.GetBuffer();
        }

        public double Volume
        {
            get { return Channel.Volume; }
            set { Channel.Volume = (float)value; }
        }

        public float Pan
        {
            get { return Channel.Pan; }
            set { Channel.Pan = value; }
        }

        public override WaveFormat WaveFormat
        {
            get { return sourceStream.WaveFormat; }
        }

        public override long Length
        {
            get { return sourceStream.Length; }
        }

        public double Frequency { get; set; }

        public override long Position
        {
            get { return sourceStream.Position; }
            set { sourceStream.Position = value; }
        }

        public int GetNextInterval()
        {
            BeatCollection.Enumerator.MoveNext();
            int result = BeatCollection.Enumerator.Current;
            // handle random mute
            if (Metronome.GetInstance().IsRandomMute)
            {
                int rand = (int)(Metronome.Rand.NextDouble() * 100);
                if (rand < Metronome.GetInstance().RandomMutePercent)
                {
                    BeatCollection.Enumerator.MoveNext();
                    result += BeatCollection.Enumerator.Current;
                }
            }
            return result;
        }

        public void SetOffset(double value)
        {
            offsetRemainder = ((int)value) - value;
            initialOffset = (int)value * 4;
            // offsetRemainder = value - initialOffset;
            hasOffset = true;
        }

        public double GetOffset()
        {
            return initialOffset + offsetRemainder;
        }

        protected int initialOffset = 0;
        protected double offsetRemainder = 0f;
        protected bool hasOffset = false;

        public int ByteInterval;

        public override int Read(byte[] buffer, int offset, int count)
        {
            int bytesCopied = 0;
            int cacheSize = cache.Length;

            while (bytesCopied < count)
            {
                if (hasOffset)
                {
                    int subtract = initialOffset > count - bytesCopied ? count - bytesCopied : initialOffset;
                    initialOffset -= subtract;
                    bytesCopied += subtract;

                    if (initialOffset == 0)
                    {
                        Layer.Remainder += offsetRemainder;
                        hasOffset = false;
                    }
                }

                if (ByteInterval == 0)
                {
                    ByteInterval = GetNextInterval();
                    cacheIndex = 0;
                }

                if (cacheIndex < cacheSize) // play from the sample
                {
                    // have to keep 4 byte alignment throughout
                    int offsetMod = (offset + bytesCopied) % 4;
                    if (offsetMod != 0)
                    {
                        bytesCopied += (4 - offsetMod);
                        ByteInterval -= (4 - offsetMod);
                    }

                    int chunkSize = new int[] { cacheSize - cacheIndex, ByteInterval, count - bytesCopied }.Min();

                    int chunkSizeMod = chunkSize % 4;
                    if (chunkSizeMod != 0)
                    {
                        chunkSize += (4 - chunkSizeMod);
                        ByteInterval -= (4 - chunkSizeMod);
                    }

                    if (ByteInterval <= 0)
                    {
                        int carry = ByteInterval < 0 ? ByteInterval : 0;
                       
                        ByteInterval = GetNextInterval();
                        ByteInterval += carry;
                        cacheIndex = 0;
                    }

                    // dont read more than cache size
                    if (offset + bytesCopied + chunkSize > cacheSize - cacheIndex)
                    {
                        chunkSize = cacheSize - cacheIndex - (offset + bytesCopied);
                        chunkSize -= chunkSize % 4;
                    }

                    if (chunkSize >= 4)
                    {
                        Array.Copy(cache, cacheIndex, buffer, offset + bytesCopied, chunkSize);
                        cacheIndex += chunkSize;
                        bytesCopied += chunkSize;
                        ByteInterval -= chunkSize;
                    }
                }
                else // silence
                {
                    int smallest = Math.Min(ByteInterval, count - bytesCopied);

                    ByteInterval -= smallest;
                    bytesCopied += smallest;
                }
            }
            return count;
        }
    }


    public interface IStreamProvider
    {
        bool IsPitch { get; }

        int GetNextInterval();

        double Volume { get; set; }

        float Pan { get; set; }

        double Frequency { get; set; }

        void Dispose();

        int BytesPerSec { get; set; }

        void SetOffset(double value);

        double GetOffset();

        Layer Layer { get; set; }

        SourceBeatCollection BeatCollection { get; set; }

        WaveFormat WaveFormat { get; }

    }


    public class SourceBeatCollection : IEnumerable<int>
    {
        Layer Layer;
        double[] Beats;
        public IEnumerator<int> Enumerator;
        bool isWav;

        public SourceBeatCollection(Layer layer, double[] beats, IStreamProvider src)
        {
            Layer = layer;
            Beats = beats.Select((x) => BeatCell.ConvertFromBpm(x, src)).ToArray();
            Enumerator = GetEnumerator();
            isWav = src.WaveFormat.AverageBytesPerSecond == 64000;
        }

        public IEnumerator<int> GetEnumerator()
        {
            for (int i=0;; i++)
            {
                if (i == Beats.Count()) i = 0; // loop over collection

                double bpm = Beats[i];//BeatCell.ConvertFromBpm(Beats[i], BytesPerSec);

                int whole = (int)bpm;
                
                Layer.Remainder += bpm - whole; // add to layer's remainder accumulator
                
                if (Layer.Remainder >= 1) // fractional value exceeds 1, add it to whole
                {
                    whole++;
                    Layer.Remainder -= 1;
                }

                if (isWav) whole *= 4; // multiply for wav files. 4 bytes per sample
        
                yield return whole;
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

    }
}
