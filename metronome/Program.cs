using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Media;
using System.Threading;
using System.IO;
using System.Diagnostics;
using System.Windows.Media;
using NAudio;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;


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


    class Metronome : IDisposable
    {
        protected MultimediaTimer MetTimer = new MultimediaTimer();
        protected long ElapsedMilliseconds = 0;

        static Metronome Instance;

        protected int LayerIndex = 0;
        protected List<Layer> Layers = new List<Layer>();

        private Metronome()
        {
            MetTimer.Interval = 1;
            MetTimer.Resolution = 0;
            MetTimer.Elapsed += Tick;
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
            MetTimer.Start();
        }

        public void Stop()
        {
            MetTimer.Stop();
            ElapsedMilliseconds = 0;
        }

        public int Tempo // BPM
        {
            get; set;
        }

        protected void Tick(object sender, EventArgs e)
        {
            var ls = Layers.Where((layer) => ElapsedMilliseconds == layer.NextNote).ToArray();

            //foreach (Layer layer in ls) Task.Run(() => layer.Play());
            if (ls.Any())
            {
                //Console.WriteLine(ls.Count());
                new Thread(() => {
                    foreach (Layer layer in ls) {
                        int index = layer.CurrentBeatIndex;
                        layer.PlayBeat(index);
                    }
                }).Start();
                foreach (Layer layer in ls) layer.Progress();
            }

            ElapsedMilliseconds++;
        }

        public void Dispose()
        {
            Layers.ForEach((x) => x.Dispose());
        }
    }


    class Layer : IDisposable
    {
        protected List<BeatCell> Beat = new List<BeatCell>();
        //protected List<long> BeatForecast = new List<long>();

        protected Dictionary<string, AudioSource> AudioSources = new Dictionary<string, AudioSource>();
        protected AudioSource BaseAudioSource;

        public float Remainder = 0F; // holds the accumulating fractional milliseconds.
        public long NextNote = 0;
        public int CurrentBeatIndex = 0;

        public Layer(BeatCell[] beat, string baseSourceName)
        {
            // is sample or pitch source?
            if (baseSourceName.Count() <= 5)
                BaseAudioSource = new PitchSource(baseSourceName);
            else
                BaseAudioSource = WavAudioSource.GetByName(baseSourceName);
            SetBeat(beat);
            Volume = .6f;
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

        public void PlayBeat(int index)
        {
            Beat[index].AudioSource.Play(Volume);
        }

        public void PlayCurrentBeat()
        {
            Beat[CurrentBeatIndex].AudioSource.Play(Volume);
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
    

    abstract class AudioSource : IDisposable
    {
        public abstract void Play();
        public abstract void Play(float volume);
        public abstract void Dispose();
    }


    class CustomReader : AudioFileReader
    {
        protected bool isCached = false;
        protected byte[] bufferCache;

        public CustomReader(string fileName) : base(fileName)
        {

        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (isCached)
            {
                
                //return count;
            }

            int result = base.Read(buffer, offset, count);
            if (result == 0)
            {
                isCached = true;
                //bufferCache = buffer.Clone() as byte[];
                //bufferCache = bufferCache;
            }
            return result;
        }
    }


    class WavAudioSource : AudioSource
    {
        protected CustomReader File;
        protected WasapiOut Player;

        public WavAudioSource(string source)
        {
            File = new CustomReader(source);
            Player = new WasapiOut(AudioClientShareMode.Shared, true, 7000);
            File.Volume = 1.0F;
            Player.Init(File);
        }

        static public WavAudioSource GetByName(string name)
        {
            return new WavAudioSource(WavSources[name]);
        }

        override public void Play(float volume)
        {
            File.Volume = volume;
            Play();
        }

        override public void Play()
        {
            File.Position = 0;
            Player.Play();
        }

        public void Stop()
        {
            Player.Stop();
        }

        override public void Dispose()
        {
            File.Dispose();
            Player.Dispose();
        }

        public static Dictionary<string, string> WavSources = new Dictionary<string, string>
        {
            { "snare_xstick_v3", "snare_xstick_v16.wav" },
            { "ride_center_v2", "ride_center_v8.wav" },
            { "hihat_pedal_v2", "hihat_pedal_v5.wav" }
        };
    }


    class PitchSource : AudioSource
    {
        protected SineWaveProvider32 SineWave;
        protected WaveOut Player;

        public PitchSource(string noteSymbol)
        {
            SetPitch(noteSymbol);
            SineWave = new SineWaveProvider32();
            SineWave.Frequency = Pitch;
            Player = new WaveOut();
            Player.Init(SineWave);
        }

        public float Pitch
        {
            get;
            set;
        }

        public void SetPitch(string symbol)
        {
            string note = new string(symbol.TakeWhile((x) => !char.IsNumber(x)).ToArray()).ToLower();
            if (note == string.Empty) // raw pitch value
            {
                Pitch = Convert.ToSingle(symbol);
                return;
            }
            string o = new string(symbol.SkipWhile((x) => !char.IsNumber(x)).ToArray());
            int octave;
            if (o != string.Empty) octave = Convert.ToInt32(o) - 5;
            else octave = 4;

            float index = Notes[note];
            index += octave * 12;
            Pitch = (float)(440 * Math.Pow(2, index / 12));
        }

        protected static Dictionary<string, int> Notes = new Dictionary<string, int>
        {
            { "a", 12 }, { "a#", 13 }, { "bb", 13 }, { "b", 14 }, { "c", 3 },
            { "c#", 4 }, { "db", 4 }, { "d", 5 }, { "d#", 6 }, { "eb", 6 },
            { "e", 7 }, { "f", 8 }, { "f#", 9 }, { "gb", 9 }, { "g", 10 },
            { "g#", 11 }, { "ab", 11 }
        };

        public void Stop()
        {
            Player.Stop();
        }

        public override void Play()
        {
            Player.Play();
        }

        override public void Play(float volume)
        {
            SineWave.sample = 0;
            SineWave.Amplitude = volume;
            Play();
        }

        override public void Dispose()
        {
            Player.Dispose();
        }
    }
    

    class BeatCell
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


    class SineWaveProvider32 : WaveProvider32
    {
        public int sample;

        public float Frequency { get; set; }
        float amplitude;
        public float Amplitude {
            get
            {
                return amplitude;
            }
            set
            {
                decay = value * .000133f;
                amplitude = value;
            }
        }

        protected float[] bufferCache = new float[26460/4];
        protected bool isCached = false;
        protected bool ca = false;
        protected int length;
        protected float decay;

        public SineWaveProvider32()
        {
            Frequency = 1000;
            Amplitude = 0.6f;
        }

        public override int Read(float[] buffer, int offset, int sampleCount)
        {
            if (Amplitude <= 0)
            {
                isCached = true;
                return 0;
            }
            if (isCached)
            {
                if (ca)
                {
                    ca = false;
                    return 0;
                }

                ca = true;
                return sampleCount;
            }

            int sampleRate = WaveFormat.SampleRate;
            for (int i=0; i<sampleCount; i++)
            {

                Amplitude -= .00008f; //quick fade
                if (Amplitude <= 0)
                {
                    isCached = true;
                    length = offset + i;
                    return i;
                }

                bufferCache[i + offset/4] = buffer[i + offset] = (float)(Amplitude * Math.Sin((2 * Math.PI * sample * Frequency) / sampleRate));
                sample++;

                if (sample >= (int)(sampleRate/Frequency)) sample = 0;

            }
            return sampleCount;
        }
    }


    public class MultimediaTimer : IDisposable
    {
        private bool disposed = false;
        private int interval, resolution;
        private UInt32 timerId;

        // Hold the timer callback to prevent garbage collection.
        private readonly MultimediaTimerCallback Callback;

        public MultimediaTimer()
        {
            Callback = new MultimediaTimerCallback(TimerCallbackMethod);
            Resolution = 5;
            Interval = 10;
        }

        ~MultimediaTimer()
        {
            Dispose(false);
        }

        public int Interval
        {
            get
            {
                return interval;
            }
            set
            {
                CheckDisposed();

                if (value < 0)
                    throw new ArgumentOutOfRangeException("value");

                interval = value;
                if (Resolution > Interval)
                    Resolution = value;
            }
        }

        // Note minimum resolution is 0, meaning highest possible resolution.
        public int Resolution
        {
            get
            {
                return resolution;
            }
            set
            {
                CheckDisposed();

                if (value < 0)
                    throw new ArgumentOutOfRangeException("value");

                resolution = value;
            }
        }

        public bool IsRunning
        {
            get { return timerId != 0; }
        }

        public void Start()
        {
            CheckDisposed();

            if (IsRunning)
                throw new InvalidOperationException("Timer is already running");

            // Event type = 0, one off event
            // Event type = 1, periodic event
            UInt32 userCtx = 0;
            timerId = NativeMethods.TimeSetEvent((uint)Interval, (uint)Resolution, Callback, ref userCtx, 1);
            if (timerId == 0)
            {
                int error = Marshal.GetLastWin32Error();
                throw new Exception();//Win32Exception(error);
            }
        }

        public void Stop()
        {
            CheckDisposed();

            if (!IsRunning)
                throw new InvalidOperationException("Timer has not been started");

            StopInternal();
        }

        private void StopInternal()
        {
            NativeMethods.TimeKillEvent(timerId);
            timerId = 0;
        }

        public event EventHandler Elapsed;

        public void Dispose()
        {
            Dispose(true);
        }

        private void TimerCallbackMethod(uint id, uint msg, ref uint userCtx, uint rsv1, uint rsv2)
        {
            var handler = Elapsed;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }

        private void CheckDisposed()
        {
            if (disposed)
                throw new ObjectDisposedException("MultimediaTimer");
        }

        private void Dispose(bool disposing)
        {
            if (disposed)
                return;

            disposed = true;
            if (IsRunning)
            {
                StopInternal();
            }

            if (disposing)
            {
                Elapsed = null;
                GC.SuppressFinalize(this);
            }
        }
    }

    internal delegate void MultimediaTimerCallback(UInt32 id, UInt32 msg, ref UInt32 userCtx, UInt32 rsv1, UInt32 rsv2);

    internal static class NativeMethods
    {
        [DllImport("winmm.dll", SetLastError = true, EntryPoint = "timeSetEvent")]
        internal static extern UInt32 TimeSetEvent(UInt32 msDelay, UInt32 msResolution, MultimediaTimerCallback callback, ref UInt32 userCtx, UInt32 eventType);

        [DllImport("winmm.dll", SetLastError = true, EntryPoint = "timeKillEvent")]
        internal static extern void TimeKillEvent(UInt32 uTimerId);
    }
}
