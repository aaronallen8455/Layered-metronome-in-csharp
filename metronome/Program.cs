using System;
using System.Collections.Generic;
using System.Collections;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
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
            metronome.Tempo = 110f;

            // should create layers just assigning the beat code and source then add them to metronome.
            // then parse the beat. then add sources to mixer.

            var layer1 = new Layer("1,2/3,1/3", "A4");

            //var layer1 = new Layer(new BeatCell[]
            //{
            //    new BeatCell(2d)
            //}, "hihat_pedal_v5.wav");
            //layer1.Volume = .6f;
            //layer1.Pan = 1;
            //layer1.SetOffset(1d);
            //
            //var layer2 = new Layer(new BeatCell[]
            //{
            //    new BeatCell(4/5d), new BeatCell(4/5d, "f4"), new BeatCell(4/5d, "a4")
            //}, "c#4");
            //
            //var layer3 = new Layer(new BeatCell[]
            //{
            //    new BeatCell(2/3d)//, new BeatCell(2/3f), new BeatCell(1/3f)
            //}, "snare_xstick_v16.wav");
            //layer3.SetOffset(1 / 3d);
            //
            //var layer4 = new Layer(new BeatCell[]
            //{
            //    new BeatCell(1d), new BeatCell(2/3d), new BeatCell(1/3d)
            //}, "ride_center_v8.wav");
            //
            ////var layer4 = new Layer(new BeatCell[]
            ////{
            ////    new BeatCell(4/11f)
            ////}, "G5");
            ////
            ////var layer5 = new Layer(new BeatCell[]
            ////{
            ////    new BeatCell(4/17f)
            ////}, "C5");
            ////layer5.Volume = .1f;
            ////layer5.Pan = -1;
            //
            metronome.AddLayer(layer1);
            //metronome.AddLayer(layer2);
            //metronome.AddLayer(layer3);
            //metronome.AddLayer(layer4);
            //metronome.AddLayer(layer5);
            //metronome.SetRandomMute(50, 50);
            //metronome.SetSilentInterval(4d, 2d);
            //metronome.RemoveLayer(layer1);
            Thread.Sleep(2000);

            

                metronome.Play();
                Console.ReadKey();
            //Console.WriteLine(metronome.GetElapsedTime());
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
        public List<Layer> Layers = new List<Layer>();

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
                Mixer.AddMixerInput(layer.BasePitchSource);
            else
            {
                Mixer.AddMixerInput(((WavFileStream)layer.BaseAudioSource).Channel);
                // check if has a pitch stream
                if (layer.BasePitchSource != default(PitchStream))
                    Mixer.AddMixerInput(layer.BasePitchSource);
            }
            // transfer silent interval if exists
            if (IsSilentInterval)
            {
                foreach (IStreamProvider src in layer.AudioSources.Values)
                {
                    src.SetSilentInterval(AudibleInterval, SilentInterval);
                }
                layer.BaseAudioSource.SetSilentInterval(AudibleInterval, SilentInterval);

                if (!layer.IsPitch && layer.BasePitchSource != default(PitchStream))
                    layer.BasePitchSource.SetSilentInterval(AudibleInterval, SilentInterval);
            }

            Layers.Add(layer);
        }

        public void RemoveLayer(Layer layer)
        {
            Layers.Remove(layer);
            // remove from mixer
            // have to remove all, then add back in
            Mixer.RemoveAllMixerInputs();
            foreach (Layer item in Layers.ToArray())
            {
                Layers.Remove(item);
                AddLayer(item);
            }
            
            layer.Dispose();
        }

        public void Play()
        {
            Player.Play();
        }

        public void Stop()
        {
            Player.Pause();
            // reset components
            foreach (Layer layer in Layers)
            {
                layer.Reset();
            }
        }

        public void Pause()
        {
            Player.Pause();
        }

        public TimeSpan GetElapsedTime()
        {
            return Player.PlaybackPosition;
        }

        public float Tempo // in BPM
        {
            get; set;
        }

        public float Volume
        {
            get { return Player.Volume; }
            set { Player.Volume = value; }
        }

        public static Random Rand = new Random();

        public bool IsRandomMute = false;

        public int RandomMutePercent;
        public int RandomMuteSeconds = 0;

        public void SetRandomMute(int percent, int seconds = 0)
        {
            RandomMutePercent = percent <= 100 && percent >= 0 ? percent : 0;
            IsRandomMute = RandomMutePercent > 0 ? true : false;
            RandomMuteSeconds = seconds;
        }

        public bool IsSilentInterval = false;

        public double AudibleInterval;
        public double SilentInterval;

        public void SetSilentInterval(double audible, double silent)
        {
            if (audible > 0 && silent > 0)
            {
                AudibleInterval = audible;
                SilentInterval = silent;
                IsSilentInterval = true;
                // set for all audio sources
                foreach (Layer layer in Layers)
                {
                    // for each audio source in the layer
                    foreach (IStreamProvider src in layer.AudioSources.Values)
                    {
                        src.SetSilentInterval(audible, silent);
                    }
                    layer.BaseAudioSource.SetSilentInterval(audible, silent);

                    if (!layer.IsPitch && layer.BasePitchSource != default(PitchStream))
                        layer.BasePitchSource.SetSilentInterval(audible, silent);
                }
            }
            else
                IsSilentInterval = false;
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
        public string ParsedString;
        public double Remainder = .0; // holds the accumulating fractional milliseconds.
        public double Offset = 0; // in BPM
        protected string BaseSourceName;

        public Layer(BeatCell[] beat, string baseSourceName)
        {
            SetBaseSource(baseSourceName);
            SetBeat(beat);
            Volume = .6f;
        }

        public Layer(string beat, string baseSourceName)
        {
            SetBaseSource(baseSourceName);
            Parse(beat); // parse the beat code into this layer
            Volume = .6f;
        }

        public void Parse(string beat)
        {
            ParsedString = beat;
            // todo: parse the string
            // remove comments
            beat = Regex.Replace(beat, @"!.*?!", "");

            if (beat.Contains('$'))
            {
                // prep single cell repeat on ref if exists
                beat = Regex.Replace(beat, @"($[\ds]+)((\d\))", "[$1]$2");
                //resolve beat referencing
                while (beat.Contains('$'))
                {
                    string refBeat;
                    // is a self reference?
                    if (beat[beat.IndexOf('$') + 1].ToString().ToLower() == "s")
                    {
                        refBeat = Regex.Replace(ParsedString, @"!.*?!", "");
                    }
                    else
                    {
                        //get the index of the referenced beat, if exists
                        int refIndex = int.Parse(Regex.Match(beat, @"\$[\d]+").Value.Substring(1));
                        // does referenced beat exist?
                        refIndex = Metronome.GetInstance().Layers.ElementAtOrDefault(refIndex) == null ? 0 : refIndex;
                        refBeat = Regex.Replace(Metronome.GetInstance().Layers[refIndex].ParsedString, @"!.*?!", "");
                    }
                    // remove sound source modifiers
                    refBeat = Regex.Replace(refBeat, @"@[a-gA-G]?[#b]?\d+", "");
                    // remove references and their innermost nest from the referenced beat
                    while (refBeat.Contains('$'))
                    {
                        if (Regex.IsMatch(refBeat, @"[[{][^[{\]}]*\$[^[{\]}]*[\]}][^\]},]*"))
                            refBeat = Regex.Replace(refBeat, @"[[{][^[{\]}]*\$[^[{\]}]*[\]}][^\]},]*", "");
                        else
                            refBeat = Regex.Replace(refBeat, @"\$[\ds]+", ""); // straight up replace
                    }
                    // clean out empty cells
                    refBeat = Regex.Replace(refBeat, @",,", ",");
                    refBeat = Regex.Replace(refBeat, @",$", "");

                    // replace in the refBeat
                    var match = Regex.Match(beat, @"$[\ds]+");
                    beat = beat.Substring(0, match.Index) + refBeat + beat.Substring(match.Index + match.Length);
                }
            }

            // allow 'x' to be multiply operator
            beat = beat.Replace('x', '*');
            beat = beat.Replace('X', '*');

            // handle group multiply
            while (beat.Contains('{'))
            {
                var match = Regex.Match(beat, @"\{([^}]*)}([^,\]]+)"); // match the inside and the factor
                // insert the multiplication
                string inner = Regex.Replace(match.Groups[1].Value, @"(?=([,+-]|$))", "*" + match.Groups[2].Value);
                // switch the multiplier to be in front of pitch modifiers
                inner = Regex.Replace(inner, @"(@[a-gA-G]?#?\d+)(\*[\d.*/]+)", "$2$1");
                // insert into beat
                beat = beat.Substring(0, match.Index) + inner + beat.Substring(match.Index + match.Length);
            }

            // handle single cell repeats
            while (Regex.IsMatch(beat, @"[^\]]\(\d+\)"))
            {
                var match = Regex.Match(beat, @"([.\d+\-/*]+@?[a-gA-G]#?)\((\d+)\)([\d\-+/*.]*)");
                StringBuilder result = new StringBuilder(beat.Substring(0, match.Index));
                //result.Append(match.Groups[1].Value, int.Parse(match.Groups[2].Value));
                for (int i=0; i<int.Parse(match.Groups[2].Value); i++)
                {
                    result.Append(match.Groups[1].Value);
                    // add comma or last term modifier
                    if (i == int.Parse(match.Groups[2].Value) - 1)
                    {
                        result.Append("+0").Append(match.Groups[3].Value);
                    }
                    else result.Append(",");
                }
                // insert into beat
                beat = result.Append(beat.Substring(match.Index + match.Length)).ToString();
            }

            // handle multi-cell repeats
            while (beat.Contains('['))
            {
                var match = Regex.Match(beat, @"\[([^\]]+?)\]\(?(\d+)\)?([\d\-+/*.]*)");
                StringBuilder result = new StringBuilder(beat.Substring(0, match.Index));
                int itr = int.Parse(match.Groups[2].Value);
                for (int i=0; i<itr; i++)
                {
                    result.Append(match.Groups[1].Value);
                    if (i == itr - 1)
                    {
                        result.Append("+0").Append(match.Groups[3].Value);
                    }
                    else result.Append(",");
                }
            }
            BeatCell[] cells = beat.Split(',').Select((x) =>
            {
                var match = Regex.Match(x, @"([\d.+\-/*]+)@?(.*)");
                return new BeatCell(match.Groups[1].Value, match.Groups[2].Value);
            }).ToArray();

            SetBeat(cells);
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
            Offset = offset;

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

        public void Reset()
        {
            Remainder = 0;
            foreach (IStreamProvider src in AudioSources.Values)
            {
                src.Reset();
            }
            BaseAudioSource.Reset();
            if (!IsPitch && BasePitchSource != default(PitchStream))
                BasePitchSource.Reset(); 
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

        public BeatCell(string beat, string sourceName = "")
        {
            string operators = "";
            for (int i=0; i<beat.Length; i++)
            {
                if (beat[i] == '+' || beat[i] == '-' || beat[i] == '*' || beat[i] == '/')
                    operators += beat[i];
            }
            double[] numbers = beat.Split(new char[] { '+', '-', '*', '/' }).Select((x)=>Convert.ToDouble(x)).ToArray();

            // do mult and div
            while (operators.IndexOfAny(new[]{ '*', '/'}) > -1)
            {
                int index = operators.IndexOfAny(new[] { '*', '/' });

                switch (operators[index])
                {
                    case '*':
                        numbers[index] *= numbers[index + 1];
                        numbers = numbers.Take(index + 1).Concat(numbers.Skip(index + 2)).ToArray();
                        break;
                    case '/':
                        numbers[index] /= numbers[index + 1];
                        numbers = numbers.Take(index + 1).Concat(numbers.Skip(index + 2)).ToArray();
                        break;
                }
                operators = operators.Remove(index, 1);
            }
            // do addition and subtraction
            while (operators.IndexOfAny(new[] { '+', '-' }) > -1)
            {
                int index = operators.IndexOfAny(new[] { '+', '-' });

                switch (operators[index])
                {
                    case '+':
                        numbers[index] += numbers[index + 1];
                        numbers = numbers.Take(index + 1).Concat(numbers.Skip(index + 2)).ToArray();
                        break;
                    case '-':
                        numbers[index] -= numbers[index + 1];
                        numbers = numbers.Take(index + 1).Concat(numbers.Skip(index + 2)).ToArray();
                        break;
                }
                operators = operators.Remove(index, 1);
            }

            SourceName = sourceName;
            Bpm = numbers[0];
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
        private float nSample;
        
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
            // hand silent interval

            if (IsSilentIntervalSilent())
            {
                previousByteInterval = result;
                return result;
            }
            // handle random mute
            if (IsRandomMuted())
            {
                BeatCollection.Enumerator.MoveNext();
                result += BeatCollection.Enumerator.Current;
                currentlyMuted = true;
            }
            else currentlyMuted = false;

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
                randomMuteCountdown = randomMuteCountdownTotal = Metronome.GetInstance().RandomMuteSeconds * BytesPerSec;
            }

            int rand = (int)(Metronome.Rand.NextDouble() * 100);

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
        protected bool lastIntervalMuted = false; // used to cycle pitch if the last interval was randomly muted.

        protected int previousByteInterval;
        protected int ByteInterval;

        double previousSample;

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
                    //if (lastIntervalMuted)
                    //{
                    //    GetNextFrequency();
                    //    lastIntervalMuted = false;
                    //}

                    Frequency = GetNextFrequency();
                    ByteInterval = GetNextInterval();
                    if (!silentIntvlSilent && !currentlyMuted)
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
                    sampleValue = previousSample = Gain * Math.Sin(nSample * multiple);
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

        public void Reset()
        {
            BeatCollection.Enumerator = BeatCollection.GetEnumerator();
            ByteInterval = 0;
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
            cacheIndex = 0;
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

            if (IsSilentIntervalSilent())
            {
                previousByteInterval = result;
                return result;
            }
            // handle random mute
            if (IsRandomMuted())
            {
                BeatCollection.Enumerator.MoveNext();
                result += BeatCollection.Enumerator.Current;
                currentlyMuted = true;
            }
            else currentlyMuted = false;

            previousByteInterval = result;

            return result;
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
                        currentSlntIntvl += 1;
                        SilentIntervalRemainder -= 1;
                    }
                } while (currentSlntIntvl < 0);
            }

            return silentIntvlSilent;
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
                randomMuteCountdown = randomMuteCountdownTotal = Metronome.GetInstance().RandomMuteSeconds * BytesPerSec;
            }

            int rand = (int)(Metronome.Rand.NextDouble() * 100);
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

        public void SetOffset(double value)
        {
            offsetRemainder = ((int)value) - value;
            initialOffset = (int)value * 4;
            hasOffset = true;
        }

        public double GetOffset()
        {
            return initialOffset + offsetRemainder;
        }

        protected int initialOffset = 0;
        protected double offsetRemainder = 0f;
        protected bool hasOffset = false;

        protected double SilentInterval; // remaining samples in silent interval
        protected double AudibleInterval; // remaining samples in audible interval
        protected int currentSlntIntvl;
        protected bool silentIntvlSilent = false;
        protected double SilentIntervalRemainder; // fractional portion

        public void SetSilentInterval(double audible, double silent)
        {
            AudibleInterval = BeatCell.ConvertFromBpm(audible, this) * 4;
            SilentInterval = BeatCell.ConvertFromBpm(silent, this) * 4;
            currentSlntIntvl = (int)AudibleInterval - initialOffset - 4;
            SilentIntervalRemainder = audible - currentSlntIntvl + offsetRemainder;
        }

        protected int previousByteInterval = 0;

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
                    Array.Copy(new byte[subtract], 0, buffer, bytesCopied + offset, subtract);
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
                    if (!silentIntvlSilent && !currentlyMuted) cacheIndex = 0;
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
                    if (chunkSize > cacheSize - cacheIndex)
                    {
                        chunkSize = cacheSize - cacheIndex;// - (offset + bytesCopied);
                        //chunkSize -= chunkSize % 4;
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

        void Reset();

        int BytesPerSec { get; set; }

        void SetOffset(double value);

        double GetOffset();

        void SetSilentInterval(double audible, double silent);

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
