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
            var layer1 = new Layer(new BeatCell[]
            {
                new BeatCell("4000/4")
            }, "a4");
            layer1.Volume = .3f;
            
            var layer2 = new Layer(new BeatCell[]
            {
                new BeatCell("4000/5")//, new TimeInterval("1000/3"), new TimeInterval("1000/3")
            }, "c#4");
            
            var layer3 = new Layer(new BeatCell[]
            {
                new BeatCell("4000/3")
            }, "snare_xstick_v3");

            var layer4 = new Layer(new BeatCell[]
            {
                new BeatCell("4000/11")
            }, "G5");

            Metronome metronome = Metronome.GetInstance();
            metronome.AddLayer(layer1);
            metronome.AddLayer(layer2);
            metronome.AddLayer(layer3);
            metronome.AddLayer(layer4);
            
            Thread.Sleep(2000);

            //var p = new PitchSource("a4");
            //p.Play(.6f);
            //Thread.Sleep(800);
            //p.Stop();
            //p.Play(.6f);

            metronome.Start();
            Console.ReadKey();
            metronome.Stop();

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
        }

        public void Start()
        {
            Player.Play();
        }

        public void Stop()
        {
            Player.Stop();
        }

        public int Tempo // BPM
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

        protected Dictionary<string, IStreamProvider> AudioSources = new Dictionary<string, IStreamProvider>();
        protected IStreamProvider BaseAudioSource;
        protected PitchStream BasePitchSource = new PitchStream(); // only use one pitch source per layer
        public bool IsPitch;

        public float Remainder = 0F; // holds the accumulating fractional milliseconds.
        public long NextNote = 0;
        public int CurrentBeatIndex = 0;
        //todo: public Offset

        public Layer(BeatCell[] beat, string baseSourceName)
        {
            SetBaseSource(baseSourceName);
            SetBeat(beat);
            Volume = .6f;
        }

        public void SetBaseSource(string baseSourceName)
        {
            // is sample or pitch source?
            if (baseSourceName.Count() <= 5)
            {
                //var pitchSource = new PitchStream();
                BasePitchSource.SetFrequency(baseSourceName);
                BaseAudioSource = BasePitchSource; // needs to be cast back to ISampleProvider when added to mixer
                IsPitch = true;
            }
            else
            {
                BaseAudioSource = new WavFileStream(baseSourceName);
                IsPitch = false;
            }
        }

        public void SetBeat(BeatCell[] beat)
        {
            int tempo = Metronome.GetInstance().Tempo;
            List<string> sources = new List<string>();

            for (int i = 0; i < beat.Count(); i++)
            {
                beat[i].Layer = this;
                if (beat[i].SourceName != string.Empty && beat[i].SourceName.Count() > 5)
                {
                    //sources.Add(beat[i].SourceName);
                    var wavStream = new WavFileStream(beat[i].SourceName);
                    beat[i].AudioSource = wavStream;
                    AudioSources.Add(beat[i].SourceName, wavStream);
                }
                else
                {
                    if (beat[i].SourceName.Count() <= 5)
                        beat[i].AudioSource = BasePitchSource;
                    else
                        beat[i].AudioSource = BaseAudioSource;
                }
                // set beat's value based on tempo and bytes/sec
                beat[i].SetBeatValue();
            }

            Beat = beat.ToList();
        }

        public void SetByteIntervalsOnSources()
        {
            foreach (IStreamProvider src in AudioSources.Values)
            {
                Pair
            }
        }

        //public void Progress()
        //{
        //    NextNote += Beat[CurrentBeatIndex++].BeatValue;
        //    if (CurrentBeatIndex == Beat.Count) CurrentBeatIndex = 0;
        //}

        //protected float volume = .6f;
        public float Volume
        {
            //get { return volume; }
            set
            {
                foreach (IStreamProvider src in AudioSources.Values) src.Volume = value;
            }
        }

        public float Pan
        {
            set
            {
                foreach (IStreamProvider src in AudioSources.Values) src.Pan = value;
            }
        }

        public void Dispose()
        {
            foreach(IStreamProvider src in AudioSources.Values)
            {
                src.Dispose();
            }

            BaseAudioSource.Dispose();
            if (!IsPitch)
                BasePitchSource.Dispose();
        }
    }
    

    public class BeatCell
    {
        protected int Whole;
        protected float R = .0F; // fractional portion of samples
        public float Bpm; // value expressed in BPM time.
        public string SourceName;
        public Layer Layer;
        public IStreamProvider AudioSource;

        //public int BeatValue
        //{
        //    get
        //    {
        //        Layer.Remainder += R;
        //        if (Layer.Remainder >= 1)
        //        {
        //            Layer.Remainder -= 1;
        //            return Whole + 1;
        //        }
        //        return Whole;
        //    }
        //
        //    set
        //    {
        //
        //    }
        //}
        public void SetBeatValue()
        {
            // set byte interval based on tempo and audiosource sample rate
            float byteIntr = Bpm * (Metronome.GetInstance().Tempo / 60) * AudioSource.BytesPerSec;
            Whole = (int)byteIntr;
            R = byteIntr - Whole;
        }

        public BeatCell(float beat, string sourceName = "") // value of beat, ex. "1/3"
        {
            SourceName = sourceName;
            Bpm = beat;
            // check if fractional
            //if (beat.Contains('/'))
            //{
            //    long n = Convert.ToInt64(beat.Split('/').First());
            //    int Denominator = Convert.ToInt32(beat.Split('/').Last());
            //    Whole = n / Denominator;
            //
            //    int Numerator = (int) (n % Denominator);
            //    R = (float)Numerator / Denominator;
            //}
            //else
            //{
            //    // no fraction
            //    Whole = Convert.ToInt64(beat);
            //}
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

        // Generator variable
        private int nSample;

        public PitchStream()
            : this(44100, 2)
        {

        }
        
        public PitchStream(int sampleRate = 16000, int channel = 2)
        {
            waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channel);

            // Default
            Frequency = 440.0;
            Gain = .6;
            Pan = 0;
            BytesPerSec = ByteInterval = waveFormat.AverageBytesPerSecond / 8;
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

        public double Volume { get; set; }

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
            // assign next byte interval
            // change pitch if needed
        }

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

        public WaveChannel32 Channel { get; set; }

        public bool IsPitch { get { return false; } }

        public SourceBeatCollection BeatCollection { get; set; }

        public int BytesPerSec { get; set; }

        public WavFileStream(string fileName)
        {
            sourceStream = new WaveFileReader(fileName);
            Channel = new WaveChannel32(this);
            BytesPerSec = ByteInterval = sourceStream.WaveFormat.AverageBytesPerSecond;
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

        /// <summary>
        /// Return source stream's wave format
        /// </summary>
        public override WaveFormat WaveFormat
        {
            get { return sourceStream.WaveFormat; }
        }

        /// <summary>
        /// LoopStream simply returns
        /// </summary>
        public override long Length
        {
            get { return sourceStream.Length; }
        }

        public double Frequency { get; set; }

        /// <summary>
        /// LoopStream simply passes on positioning to source stream
        /// </summary>
        public override long Position
        {
            get { return sourceStream.Position; }
            set { sourceStream.Position = value; }
        }

        public void NextInterval()
        {
            // set ByteInterval to next interval
        }

        public int ByteInterval;

        public override int Read(byte[] buffer, int offset, int count)
        {
            int totalBytesRead = 0;

            while (totalBytesRead < count)
            {
                int limit = Math.Min(count, ByteInterval);
                // read file for complete count, or if the file is longer than interval, just read for interval.
                int bytesRead = sourceStream.Read(buffer, offset + totalBytesRead, limit - totalBytesRead);

                ByteInterval -= bytesRead;
                // is end of interval?
                if (ByteInterval == 0)
                {
                    sourceStream.Position = 0;
                    //ByteInterval = BytesPerSec;
                    NextInterval();
                }
                // we hit the end of the file, fill remaining spots with null
                limit = Math.Min(count, ByteInterval);
                if (bytesRead < count)
                {
                    // fill with zeros
                    for (int i = 0; i < limit - bytesRead; i++)
                    {
                        buffer[offset + totalBytesRead + bytesRead + i] = 0;
                        ByteInterval--;
                    }
                    bytesRead += limit - bytesRead;

                    if (ByteInterval == 0)
                    {
                        //ByteInterval = BytesPerSec;
                        NextInterval();
                        sourceStream.Position = 0;
                    }
                }

                totalBytesRead += bytesRead;
            }
            return count;
        }

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

        SourceBeatCollection BeatCollection { get; set; }

        //WaveStream Channel { get; set; }
    }


    public class SourceBeatCollection : IEnumerable<int>
    {
        Layer Layer;
        float[] Beats;

        public SourceBeatCollection(Layer layer, float[] beats)
        {
            Layer = layer;
            Beats = beats;
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


    //public class WavePlayer
    //{
    //    public WaveFileReader Reader;
    //    public WaveChannel32 Channel { get; set; }
    //
    //    public long FileSize;
    //
    //    string FileName { get; set; }
    //
    //    public WavePlayer(string FileName)
    //    {
    //        this.FileName = FileName;
    //        Reader = new WaveFileReader(FileName);
    //        WaveStream streamer = new WavFileStream(Reader);
    //        FileSize = Reader.Length;
    //
    //        Channel = new WaveChannel32(streamer) { PadWithZeroes = true };
    //    }
    //
    //    public void Dispose()
    //    {
    //        if (Channel != null)
    //        {
    //            Channel.Dispose();
    //            Reader.Dispose();
    //        }
    //    }
    //
    //}
}
