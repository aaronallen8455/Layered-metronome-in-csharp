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

/**
 * Layer class will have a method for parsing fraction components
 * every {denominator} add {numerator} miliseconds
 */

namespace Metronome
{
    class Program
    {
        

        static void Main(string[] args)
        {
            //WasapiOut test = new WasapiOut(AudioClientShareMode.Exclusive, 1);
            //IWaveProvider pro = new WaveFileReader();
            //test.Init(new );
            //test.Play();

            //IWavePlayer wavePlayer = new WasapiOut(AudioClientShareMode.Shared, true, 30);
            //AudioFileReader audioFile = new AudioFileReader("snare_xstick_v16.wav");
            //audioFile.Volume = 1.0f;
            //wavePlayer.Init(audioFile);
            //wavePlayer.Play();
            //Thread.Sleep(1000);
            //audioFile.Position = 0;
            //wavePlayer.Play();

            var ride = new WaveAudioSource("ride_center_v8.wav");
            var stick = new WaveAudioSource("snare_xstick_v16.wav");
            var hihat = new WaveAudioSource("hihat_pedal_v5.wav");
            
            var layer1 = new Layer(ride);
            layer1.SetBeat(new TimeInterval[]
            {
                new TimeInterval("1000")
            });
            
            var layer2 = new Layer(stick);
            layer2.SetBeat(new TimeInterval[]
            {
                new TimeInterval("4000/8"), new TimeInterval("1000/3"), new TimeInterval("1000/3")
            });
            
            var layer3 = new Layer(hihat);
            layer3.SetBeat(new TimeInterval[]
            {
                new TimeInterval("4000/3")
            });
            
            Metronome metronome = Metronome.GetInstance();
            metronome.AddLayer(layer1);
            metronome.AddLayer(layer2);
            //metronome.AddLayer(layer3);
            
            Thread.Sleep(2000);
            
            metronome.Start();
            Console.ReadKey();
            metronome.Stop();
            
            ride.Dispose();
            stick.Dispose();
            hihat.Dispose();


        }
    }


    class Metronome
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
                new Thread(() => { foreach (Layer layer in ls) layer.Play(); }).Start();
                foreach (Layer layer in ls) layer.Progress();
            }

            ElapsedMilliseconds++;
        }
    }


    class Layer
    {
        protected List<TimeInterval> Beat = new List<TimeInterval>();
        //protected List<long> BeatForecast = new List<long>();

        protected AudioSource AudioSource;

        public Layer(AudioSource audioSource)
        {
            AudioSource = audioSource;
        }

        public void SetBeat(TimeInterval[] beat)
        {
            Beat = beat.ToList();
            //BeatForecast = beat.ToList();
        }

        public long NextNote = 0;
        protected int BeatIndex = 0;

        public void Progress()
        {
            NextNote += Beat[BeatIndex++].Milliseconds;
            if (BeatIndex == Beat.Count) BeatIndex = 0;
        }

        public void Play()
        {
            AudioSource.Play();
            //Progress();
        }
    }
    

    abstract class AudioSource : IDisposable
    {
        public abstract void Play();
        public abstract void Dispose();
    }


    class WaveAudioSource : AudioSource
    {
        protected const int Count = 1;

        //protected Stream[] Files = new Stream[Count]; // keep spare streams on hand for concurent plays
        //protected WaveFileReader[] Readers = new WaveFileReader[Count];
        //protected DirectSoundOut[] Outputs = new DirectSoundOut[Count];
        protected AudioFileReader[] Files = new AudioFileReader[Count];
        protected WasapiOut[] Players = new WasapiOut[Count];

        protected int Current = 0;

        public WaveAudioSource(string source)
        {
            for (int i=0; i<Count; i++)
            {
                Files[i] = new AudioFileReader(source);
                Players[i] = new WasapiOut(AudioClientShareMode.Shared, true, 7000);
                Files[i].Volume = 1.0f;
                Players[i].Init(Files[i]);


                // init
                //Files[i] = File.OpenRead(source);
                //Readers[i] = new WaveFileReader(Files[i]);
                //Outputs[i] = new DirectSoundOut(37);
                //Outputs[i].Init(new WaveChannel32(Readers[i]));
                //Outputs[i].Play();
            }
        }

        override public void Play()
        {
            Files[Current].Position = 0;
            Players[Current].Play();

            //Readers[Current].Position = 0;
            ////int c = Current;
            ////Task.Run(() => Outputs[c].Play());
            ////Outputs[Current].Play();
            Current++;
            if (Current == Count) Current = 0;
        }

        public void Stop()
        {
            foreach(IWavePlayer player in Players)
            {
                player.Stop();
            }
        }

        override public void Dispose()
        {
            for (int i=0; i<Count; i++)
            {
                Files[i].Dispose();
                Players[i].Dispose();
            }
        }
    }


    class TimeInterval
    {
        private long Whole;
        private int Numerator;
        private int Denominator;
        private int I = 0; // for iterating over denominator

        public long Milliseconds
        {
            get
            {
                if (Numerator > 0)
                {
                    I++;
                    if (I == Denominator)
                    {
                        I = 0;
                        return Whole + Numerator;
                    }
                }
                return Whole;
            }
        }

        public TimeInterval(string beat) // number of ms, ex. "1000/3"
        {
            // check if fractional
            if (beat.Contains('/'))
            {
                long n = Convert.ToInt64(beat.Split('/').First());
                Denominator = Convert.ToInt32(beat.Split('/').Last());
                Whole = n / Denominator;
                Numerator = (int) (n % Denominator);
            }
            else
            {
                // no fraction
                Whole = Convert.ToInt64(beat);
                Numerator = Denominator = 0;
            }
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
