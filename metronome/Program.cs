using System;
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
            metronome.Tempo = 90;

            var layer1 = new Layer(new BeatCell[]
            {
                new BeatCell(1f)
            }, "a4");
            layer1.Volume = .6f;
            layer1.Pan = 1;
            
            //var layer2 = new Layer(new BeatCell[]
            //{
            //    new BeatCell(4/5f)//, new TimeInterval("1000/3"), new TimeInterval("1000/3")
            //}, "c#4");

            var layer3 = new Layer(new BeatCell[]
            {
                new BeatCell(4/3f)//, new BeatCell(2/3f), new BeatCell(1/3f)
            }, "snare_xstick_v16.wav");
            
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
            //metronome.AddLayer(layer2);
            metronome.AddLayer(layer3);
            //metronome.AddLayer(layer4);
            //metronome.AddLayer(layer5);
            
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
                BasePitchSource.SetFrequency(baseSourceName);
                BaseAudioSource = BasePitchSource; // needs to be cast back to ISampleProvider when added to mixer
                IsPitch = true;
            }
            else
            {
                BaseAudioSource = new WavFileStream(baseSourceName);
                IsPitch = false;
            }
            //BasePitchSource.Layer = this;
        }

        public void SetOffset(float offset)
        {
            foreach (IStreamProvider src in AudioSources.Values)
            {
                double current = src.GetOffset();
                double add = BeatCell.ConvertFromBpm(offset, src);
                src.SetOffset(current + add);
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
                        // check if basepitch source exists
                        if (BasePitchSource == default(PitchStream))
                        {
                            BasePitchSource = new PitchStream();
                            BasePitchSource.SetFrequency(beat[i].SourceName);
                        }
                        beat[i].AudioSource = BasePitchSource;
                    }
                    else
                    {
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
                            cells[cells.Count - 1] += BeatCell.ConvertFromBpm(accumulator, Beat[i].AudioSource);
                            accumulator = 0f;
                        }
                        cells.Add(Beat[p].ByteInterval);
                    }
                    else accumulator += Beat[p].Bpm;

                    // job done if current beat is one before the outer beat.
                    if (p == i - 1 || (i == 0 && p == Beat.Count - 1))
                    {
                        cells[cells.Count-1] += BeatCell.ConvertFromBpm(accumulator, Beat[i].AudioSource);
                        break;
                    }
                }
                completed.Add(Beat[i].AudioSource);
                
                Beat[i].AudioSource.BeatCollection = new SourceBeatCollection(this, cells.ToArray());
            }
        }

        protected void SetInitialOffset(float value, IStreamProvider source)
        {

        }
        //public void Progress()
        //{
        //    NextNote += Beat[CurrentBeatIndex++].BeatValue;
        //    if (CurrentBeatIndex == Beat.Count) CurrentBeatIndex = 0;
        //}

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

        static public double ConvertFromBpm(double bpm, IStreamProvider source)
        {
            return bpm * (60 / Metronome.GetInstance().Tempo) * source.BytesPerSec;
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

        //public PitchStream()
        //    : this(44100, 2)
        //{
        //
        //}
        
        public PitchStream(int sampleRate = 16000, int channel = 2)
        {
            waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channel);

            // Default
            Frequency = 440.0;
            Volume = .6f;
            Pan = 0;
            BytesPerSec = waveFormat.AverageBytesPerSecond / 8;
        }

        public void SetFrequency(string symbol)
        {
            string note = new string(symbol.TakeWhile((x) => !char.IsNumber(x)).ToArray()).ToLower();
            if (note == string.Empty) // raw pitch value
            {
                Frequency = Convert.ToSingle(symbol);
                return;
            }
            string o = new string(symbol.SkipWhile((x) => !char.IsNumber(x)).ToArray());
            int octave;
            if (o != string.Empty) octave = Convert.ToInt32(o) - 5;
            else octave = 4;

            float index = Notes[note];
            index += octave * 12;
            Frequency = (float)(440 * Math.Pow(2, index / 12));
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

        public double Frequency { get; set; }

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

        public void NextInterval()
        {
            BeatCollection.Enumerator.MoveNext();
            ByteInterval = BeatCollection.Enumerator.Current;
            Console.WriteLine($"pitch {ByteInterval}");
        }

        public void SetOffset(double value)
        {
            InitialOffset = (int)value;
            OffsetRemainder = value - InitialOffset;
            hasOffset = true;
        }

        public double GetOffset()
        {
            return InitialOffset + OffsetRemainder;
        }

        protected int InitialOffset = 0; // time to wait before reading source.
        protected double OffsetRemainder = 0f;
        protected bool hasOffset = false;

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
                if (hasOffset && InitialOffset > 0)
                {
                    for (int i=0; i<waveFormat.Channels; i++)
                    {
                        buffer[outIndex++] = 0;
                    }
                    InitialOffset--;
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
                    Gain = Volume;
                    nSample = 0;
                    NextInterval();
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
        WaveStream sourceStream;
        MemoryStream Ms;

        public WaveChannel32 Channel { get; set; }

        public bool IsPitch { get { return false; } }

        public Layer Layer { get; set; }

        public SourceBeatCollection BeatCollection { get; set; }

        public int BytesPerSec { get; set; }

        public WavFileStream(string fileName)
        {
            sourceStream = new WaveFileReader(fileName); //todo: use memory stream instead of file?
            Channel = new WaveChannel32(this);
            BytesPerSec = sourceStream.WaveFormat.AverageBytesPerSecond;

            Ms = new MemoryStream(File.ReadAllBytes(fileName));

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

        public void NextInterval()
        {
            BeatCollection.Enumerator.MoveNext();
            ByteInterval = BeatCollection.Enumerator.Current;
            Console.WriteLine($"wav {ByteInterval}");
        }

        public void SetOffset(double value)
        {
            initialOffset = (int)value;
            offsetRemainder = value = initialOffset;
            hasOffset = true;
        }

        public double GetOffset()
        {
            return initialOffset + offsetRemainder;
        }

        protected int initialOffset = 0;
        protected double offsetRemainder = 0f;
        protected bool hasOffset = false;

        int leftOver = 0;

        public int ByteInterval;

        public override int Read(byte[] buffer, int offset, int count)
        {
            int totalBytesRead = 0;
        
            while (totalBytesRead < count)
            {
                // account for offset
                if (hasOffset && initialOffset != 0)
                {
                    int size = Math.Min(count, initialOffset);
                    for (int i=0; i<size; i++)
                    {
                        buffer[offset + totalBytesRead + i] = 0;
                    }
                    initialOffset -= size;
                    totalBytesRead += size;
        
                    if (initialOffset == 0)
                    {
                        Layer.Remainder += offsetRemainder;
                        hasOffset = false;
                    }
                    else
                        continue;
        
                    if (totalBytesRead == count) break;
                }
        
                int limit;
                //int leftOver = 0;
                if (leftOver > 0)
                {
                    ByteInterval += leftOver;
                    leftOver = 0;
                }

                if (ByteInterval < count - totalBytesRead) // bytes are aligned in 4
                {
                    leftOver += ByteInterval % 4; // save the leftovers
                    ByteInterval -= leftOver;
        
                    if (buffer.Count() - totalBytesRead < ByteInterval)
                    {
                        //leftOver += ByteInterval - totalBytesRead;
                        //ByteInterval = totalBytesRead;
                        limit = buffer.Count() - totalBytesRead;
                    }
                    else limit = ByteInterval;
                }
                else limit = count - totalBytesRead;

                //int limit = Math.Min(count, ByteInterval);
                // read file for complete count, or if the file is longer than interval, just read for interval.
                int bytesRead = Ms.Read(buffer, offset + totalBytesRead, limit);

                bool eof = bytesRead == 0; // end of file was reached
        
                ByteInterval -= bytesRead;
        
                // fill left-overs with zeros
                //if (leftOver > 0 && eof)
                //{
                //        //for (int i=0; i< leftOver; i++)
                //        //{
                //        //    buffer[offset + totalBytesRead + bytesRead + i] = 0;
                //        //}
                //    //bytesRead += leftOver;
                //}
        
                // is end of interval?
                if (ByteInterval == 0)
                {
                    //sourceStream.Position = 0;
                    Ms.Position = 0;
                    NextInterval();
                }
                // we hit the end of the file, fill remaining spots with null
                limit = Math.Min(count, ByteInterval);
                if (bytesRead < count && eof)
                {
                    // fill with zeros
                    //for (int i = 0; i < limit - bytesRead - totalBytesRead; i++)
                    //{
                    //    buffer[offset + totalBytesRead + bytesRead + i] = 0;
                    //    ByteInterval--;
                    //}
                    //
                    ByteInterval -= limit - bytesRead - totalBytesRead;
                    //
                    bytesRead += limit - bytesRead;
        
                    if (ByteInterval == 0)
                    {
                        NextInterval();
                        //sourceStream.Position = 0;
                        Ms.Position = 0;
                    }
                }
        
                totalBytesRead += bytesRead;
            }
            return count;
        }

        //public override int Read(byte[] buffer, int offset, int count)
        //{
        //    int totalBytesRead = 0;
        //
        //    while (totalBytesRead < count)
        //    {
        //        // account for offset
        //        if (hasOffset && initialOffset != 0)
        //        {
        //            int size = Math.Min(count, initialOffset);
        //            for (int i = 0; i < size; i++)
        //            {
        //                buffer[offset + totalBytesRead + i] = 0;
        //            }
        //            initialOffset -= size;
        //            totalBytesRead += size;
        //
        //            if (initialOffset == 0)
        //            {
        //                Layer.Remainder += offsetRemainder;
        //                hasOffset = false;
        //            }
        //            else
        //                continue;
        //
        //            if (totalBytesRead == count) break;
        //        }
        //
        //        int leftOver = 0;
        //        if (ByteInterval < count) // bytes are aligned in 4
        //        {
        //            leftOver = ByteInterval % 4; // save the leftovers
        //            ByteInterval -= leftOver;
        //        }
        //
        //        int limit = Math.Min(count, ByteInterval);
        //        // read file for complete count, or if the file is longer than interval, just read for interval.
        //        int bytesRead = sourceStream.Read(buffer, offset + totalBytesRead, limit - totalBytesRead);
        //
        //        // fill left-overs with zeros
        //        if (leftOver > 0)
        //        {
        //            for (int i = 0; i < leftOver; i++)
        //            {
        //                buffer[offset + totalBytesRead + bytesRead + i] = 0;
        //            }
        //        }
        //
        //        ByteInterval -= bytesRead;
        //        // is end of interval?
        //        if (ByteInterval == 0)
        //        {
        //            sourceStream.Position = 0;
        //            NextInterval();
        //        }
        //        // we hit the end of the file, fill remaining spots with null
        //        limit = Math.Min(count, ByteInterval);
        //        if (bytesRead < count)
        //        {
        //            // fill with zeros
        //            for (int i = 0; i < limit - bytesRead - totalBytesRead; i++)
        //            {
        //                buffer[offset + totalBytesRead + bytesRead + i] = 0;
        //                ByteInterval--;
        //            }
        //            bytesRead += limit - bytesRead;
        //
        //            if (ByteInterval == 0)
        //            {
        //                //ByteInterval = BytesPerSec;
        //                NextInterval();
        //                sourceStream.Position = 0;
        //            }
        //        }
        //
        //        totalBytesRead += bytesRead;
        //    }
        //    return count;
        //}

        void IStreamProvider.Dispose()
        {
            Channel.Dispose();
            sourceStream.Dispose();
        }
    }


    public interface IStreamProvider
    {
        bool IsPitch { get; }

        void NextInterval();

        double Volume { get; set; }

        float Pan { get; set; }

        double Frequency { get; set; }

        void Dispose();

        int BytesPerSec { get; set; }

        void SetOffset(double value);

        double GetOffset();

        Layer Layer { get; set; }

        SourceBeatCollection BeatCollection { get; set; }

        //WaveStream Channel { get; set; }
    }


    public class SourceBeatCollection : IEnumerable<int>
    {
        Layer Layer;
        double[] Beats;
        public IEnumerator<int> Enumerator;

        public SourceBeatCollection(Layer layer, double[] beats)
        {
            Layer = layer;
            Beats = beats;
            Enumerator = GetEnumerator();
        }

        public IEnumerator<int> GetEnumerator()
        {
            for (int i=0;; i++)
            {
                if (i == Beats.Count()) i = 0; // loop over collection
        
                int whole = (int)Beats[i];
        
                Layer.Remainder += Beats[i] - whole;

                if (Layer.Remainder >= 1) // fractional value exceeds 1, add it to whole
                {
                    whole++;
                    Layer.Remainder -= 1;
                }
        
                yield return whole;
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

    }
}
