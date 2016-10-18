using System;
using System.Collections.Generic;
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

        protected Dictionary<string, IWaveProvider> AudioSources = new Dictionary<string, IWaveProvider>();
        protected IWaveProvider BaseAudioSource;
        public bool IsPitch;

        public float Remainder = 0F; // holds the accumulating fractional milliseconds.
        public long NextNote = 0;
        public int CurrentBeatIndex = 0;

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
                var pitchSource = new PitchStream();
                pitchSource.SetFrequency(baseSourceName);
                BaseAudioSource = pitchSource; // needs to be cast back to ISampleProvider
                IsPitch = true;
            }
            else
            {
                var wavePlayer = new WavePlayer(baseSourceName);
                BaseAudioSource = wavePlayer.Channel;
                IsPitch = false;
            }
        }

        public void SetBeat(BeatCell[] beat)
        {
            List<string> sources = new List<string>();

            for (int i = 0; i < beat.Count(); i++)
            {
                beat[i].Layer = this;
                if (beat[i].SourceName != string.Empty)
                {
                    sources.Add(beat[i].SourceName);
                }
            }
            // instantiate each source
            foreach (string src in sources.Distinct())
            {
                AudioSources.Add(src, WavAudioSource.GetByName(src));
            }
            // assign sources to beat cells
            for (int i = 0; i < beat.Count(); i++)
            {
                if (beat[i].SourceName == string.Empty) beat[i].AudioSource = BaseAudioSource;
                else beat[i].AudioSource = AudioSources[beat[i].SourceName];
            }

            Beat = beat.ToList();
        }

        public void Progress()
        {
            NextNote += Beat[CurrentBeatIndex++].Milliseconds;
            if (CurrentBeatIndex == Beat.Count) CurrentBeatIndex = 0;
        }

        public float Volume
        {
            get;
            set;
        }

        public void Dispose()
        {
            foreach(AudioSource src in AudioSources.Values)
            {
                src.Dispose();
            }

            BaseAudioSource.Dispose();
        }
    }
    

    public class BeatCell
    {
        protected long Whole;
        protected float R = .0F; // fractional portion
        public string SourceName;
        public Layer Layer;
        public AudioSource AudioSource;

        public long Milliseconds
        {
            get
            {
                Layer.Remainder += R;
                if (Layer.Remainder >= 1)
                {
                    Layer.Remainder -= 1;
                    return Whole + 1;
                }
                return Whole;
            }
        }

        public BeatCell(string beat, string sourceName = "") // number of ms, ex. "1000/3"
        {
            SourceName = sourceName;
            // check if fractional
            if (beat.Contains('/'))
            {
                long n = Convert.ToInt64(beat.Split('/').First());
                int Denominator = Convert.ToInt32(beat.Split('/').Last());
                Whole = n / Denominator;
                int Numerator = (int) (n % Denominator);
                R = (float)Numerator / Denominator;
            }
            else
            {
                // no fraction
                Whole = Convert.ToInt64(beat);
            }
        }
    }


    public class PitchStream : ISampleProvider, IWaveProvider
    {
        // Wave format
        private readonly WaveFormat waveFormat;

        public Layer Layer { get; set; }

        // Const Math
        private const double TwoPi = 2 * Math.PI;

        protected int BytesPerSec;

        // Generator variable
        private int nSample;

        // Sweep Generator variable
        private double phi;

        public PitchStream()
            : this(44100, 2)
        {

        }
        
        public PitchStream(int sampleRate = 16000, int channel = 2)
        {
            phi = 0;

            waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channel);

            // Default
            Frequency = 440.0;
            Gain = .6;
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

        public WaveFormat WaveFormat
        {
            get { return waveFormat; }
        }

        public double Frequency { get; set; }

        public double Gain { get; set; }

        public double Volume { get; set; }

        public void NextInterval()
        {
            // assign next byte interval
            // change pitch if needed
        }

        protected int ByteInterval;

        public int Read(byte[] buffer, int offset, int count) { return 0; }

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
                // Sin Generator
                if (Gain <= 0)
                {
                    nSample = 0;
                    sampleValue = 0;
                }
                else
                {
                    multiple = TwoPi * Frequency / waveFormat.SampleRate;
                    sampleValue = Gain * Math.Sin(nSample * multiple);
                    Gain -= .0002;
                }

                nSample++;

                // Phase Reverse Per Channel
                for (int i = 0; i < waveFormat.Channels; i++)
                {
                    buffer[outIndex++] = (float)sampleValue;
                }
                ByteInterval -= 1;
            }

            return count;
        }
    }


    public class WavFileStream : WaveStream
    {
        WaveStream sourceStream;

        protected int BytesPerSec;

        public Layer Layer;

        /// <summary>
        /// Creates a new Loop stream
        /// </summary>
        /// <param name="sourceStream">The stream to read from. Note: the Read method of this stream should return 0 when it reaches the end
        /// or else we will not loop to the start again.</param>
        public WavFileStream(WaveStream sourceStream)
        {
            this.sourceStream = sourceStream;
            BytesPerSec = ByteInterval = sourceStream.WaveFormat.AverageBytesPerSecond;
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
    }


    public class WavePlayer
    {
        public WaveFileReader Reader;
        public WaveChannel32 Channel { get; set; }

        public long FileSize;

        string FileName { get; set; }

        public WavePlayer(string FileName)
        {
            this.FileName = FileName;
            Reader = new WaveFileReader(FileName);
            WaveStream streamer = new WavFileStream(Reader);
            FileSize = Reader.Length;

            Channel = new WaveChannel32(streamer) { PadWithZeroes = true };
        }

        public void Dispose()
        {
            if (Channel != null)
            {
                Channel.Dispose();
                Reader.Dispose();
            }
        }

    }
}
