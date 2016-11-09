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
using System.Runtime.Serialization;
using System.Xml;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;


namespace Pronome
{
    class Program
    {
        static void Main(string[] args)
        {
            

            // if a future hihat closed sound is not muted, calculate its position in byte time, var hhc.
            // (number of cycles * cycle size + value of old byteInterval at start of present cycle + new byteInterval)
            // in the hihat open sound thread, determine how many bytes until hhc is reached.

            Metronome metronome = Metronome.GetInstance();
            metronome.Tempo = 90f;

            //new Layer("[.25,.25@C4,.25@A2,.25@A3,.25@Bb2,1.75@Bb3]2,[.25@F2,.25@F3,.25@D2,.25@D3,.25@Eb2,1.75@Eb3](2)-.5,1/6@Eb3,1/6@D3,1/6@Db3,.5@C3,.5@Eb3,.5@D3,.5@Ab2,.5@G2,.5@Db3,1/6@C3,1/6@F#3,1/6@F3,1/6@E3,1/6@Bb3,1/6@A3,1/3@Ab3,1/3@Eb3,1/3@B2,1/3@Bb2,1/3@A2,1/3+3@Ab2", "C3");
            //new Layer("[.5,.5+.75,.5,.75]4,.5,.25,.5,.25,.75,.25,.5,!1!.5,.5+.75,.5,.75!2!,.5,.75,.5,1,.25", WavFileStream.GetFileByName("Kick Drum V2"));
            //new Layer("[[1@0,1.5|.5]2,.25(2)]2,1@0,2,!1!1@0,.5,1.25!2.75!,.5,.75,1.5,.25(2)", WavFileStream.GetFileByName("Snare Rim V3"));
            //new Layer(".5", WavFileStream.GetFileByName("HiHat Half Center V1"));

            ////
            //var layer1 = new Layer("[1,2/3,1/3]4,{$s}2/3", "A4");
            //new Layer("1", "A5");
            //var layer2 = new Layer("1,2/3,1/3", WavFileStream.GetFileByName("Ride Center V3"));
            new Layer("1,1,1/3@19", WavFileStream.GetFileByName("HiHat Pedal V2"));

            //Metronome.Load("metronome");
            //var metronome = Metronome.GetInstance();
            metronome.SetRandomMute(50);
            metronome.Play();
            
            Console.ReadKey();
            //metronome.Stop();
            //metronome.ChangeTempo(50f);
            //Console.ReadKey();
            //metronome.ChangeTempo(38f);
            //Console.ReadKey();
            //metronome.Stop();
            //metronome.Play();
            //Console.ReadKey();
            metronome.Stop();
            metronome.Dispose();

            Console.ReadKey();
        }
    }

    /** <summary>Has player controls, tempo, global muting options, and holds layers. Singleton</summary>
     */
    [DataContract]
    public class Metronome : IDisposable
    {
        /** <sumarry>Mix the output from all audio sources.</sumarry> */
        protected MixingSampleProvider Mixer = new MixingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(16000, 2));
        /** <summary>Access the sound output device.</summary> */
        protected DirectSoundOut Player = new DirectSoundOut();

        /** <summary>The singleton instance.</summary> */
        static Metronome Instance;

        /** <summary>A collection of all the layers.</summary> */
        [DataMember]
        public List<Layer> Layers = new List<Layer>();

        /** <summary>Constructor</summary> */
        private Metronome()
        {
            Player.Init(Mixer);
        }

        /** <summary>Get the singleton instance.</summary> */
        static public Metronome GetInstance()
        {
            if (Instance == null)
                Instance = new Metronome();
            return Instance;
        }

        /** <summary>Add a layer.</summary>
         * <param name="layer">Layer to add.</param> */
        public void AddLayer(Layer layer)
        {
            // add sources to mixer
            AddSourcesFromLayer(layer);

            Layers.Add(layer);

            // re-parse all other layers that reference this beat
            int layerId = Layers.Count - 1;
            var reparse = Layers.Where(x => x != layer && x.ParsedString.Contains($"${layerId}"));
            foreach (Layer l in reparse)
            {
                l.Parse(l.ParsedString);
            }
        }

        /** <summary>Add all the audio sources from each layer.</summary>
         * <param name="layer">Layer to add sources from.</param> */
        protected void AddSourcesFromLayer(Layer layer)
        {
            // add sources to mixer
            foreach (IStreamProvider src in layer.AudioSources.Values)
            {
                if (src.IsPitch)
                {
                    Mixer.AddMixerInput((ISampleProvider)src);
                }
                else
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
        }

        /** <summary>Remove designated layer.</summary>
         * <param name="layer">Layer to remove.</param> */
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

        /** <summary>Play all layers in sync.</summary> */
        public void Play()
        {
            Player.Play();
        }

        /** <summary>Stop playing and reset positions.</summary> */
        public void Stop()
        {
            Player.Pause();
            // reset components
            foreach (Layer layer in Layers)
            {
                layer.Reset();
            }
        }

        /** <summary>Pause at current playback point.</summary> */
        public void Pause()
        {
            Player.Pause();
        }

        /** <summary>Get the elapsed playing time.</summary> */
        public TimeSpan GetElapsedTime()
        {
            return Player.PlaybackPosition;
        }

        /** <summary>The tempo in BPM.</summary> */
        protected float tempo;
        [DataMember]
        public float Tempo // in BPM
        {
            get { return tempo; }
            set { ChangeTempo(value); }
        }

        /** <summary>Change the tempo. Can be during play.</summary> */
        public void ChangeTempo(float newTempo)
        {
            if (Player.PlaybackState != PlaybackState.Stopped)
            {
                // modify the beat values and current byte intervals for all layers and audio sources.
                float ratio = Tempo / newTempo;
                Layers.ForEach(x =>
                {
                    x.AudioSources.Values.Select(a => { a.BeatCollection.MultiplyBeatValues(ratio); a.MultiplyByteInterval(ratio); return a; }).ToArray();
                    x.BaseAudioSource.BeatCollection.MultiplyBeatValues(ratio);
                    x.BaseAudioSource.MultiplyByteInterval(ratio);
                    if (!x.IsPitch && x.BasePitchSource != null)
                    {
                        x.BasePitchSource.BeatCollection.MultiplyBeatValues(ratio);
                        x.BasePitchSource.MultiplyByteInterval(ratio);
                    }
                });
            }
            tempo = newTempo;
        }

        /** <summary>The master volume.</summary> */
        [DataMember]
        public float Volume
        {
            get { return Player.Volume; }
            set { Player.Volume = value; }
        }

        /** <summary>Used for random muting.</summary> */
        public static Random Rand = new Random();

        /** <summary>Is a random muting value set?</summary> */
        [DataMember]public bool IsRandomMute = false;

        /** <summary>Percent chance that a note gets muted.</summary> */
        [DataMember]public int RandomMutePercent;
        /** <summary>Number of seconds over which the random mute percent ramps up to full value.</summary> */
        [DataMember]public int RandomMuteSeconds = 0;

        /** <summary>Set a random mute percent.</summary>
         * <param name="percent">Percent chance for muting</param>
         * <param name="seconds">Seconds ramp til full percent muting occurs.</param> */
        public void SetRandomMute(int percent, int seconds = 0)
        {
            RandomMutePercent = percent <= 100 && percent >= 0 ? percent : 0;
            IsRandomMute = RandomMutePercent > 0 ? true : false;
            RandomMuteSeconds = seconds;

            // for wav sounds, determine if first sound should be muted, if starting at beginning.
            IEnumerable<IStreamProvider> WavLayers =
                from n in Layers
                where !n.IsPitch
                select n.AudioSources.Values.Concat(new IStreamProvider[] { n.BaseAudioSource })
                into s
                from aud in s
                where !aud.IsPitch
                select aud;

            foreach(WavFileStream wfs in WavLayers)
            {
                wfs.SetInitialMuting();
            }
        }

        /** <summary>True if a silent interval is set.</summary> */
        [DataMember]public bool IsSilentInterval = false;

        /** <summary>The value in quarter notes that a beat plays audibly.</summary> */
        [DataMember]public double AudibleInterval;
        /** <summary>The value in quarter notes that a beat is silenced.</summary> */
        [DataMember]public double SilentInterval;

        /** <summary>Set an audible/silent interval.</summary>
         * <param name="audible">The value in quarter notes that a beat plays audibly.</param>
         * <param name="silent">The value in quarter notes that a beat is silenced.</param> */
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

        /** <summary>Set an audible/silent interval.</summary>
         * <param name="audible">The value in quarter notes that a beat plays audibly.</param>
         * <param name="silent">The value in quarter notes that a beat is silenced.</param> */
        public void SetSilentInterval(string audible, string silent)
        {
            SetSilentInterval(BeatCell.Parse(audible), BeatCell.Parse(silent));
        }

        /** <summary>Save the current beat to disk.</summary>
         * <param name="name">The name for this beat.</param> */
        static public void Save(string name)
        {
            var ds = new DataContractSerializer(typeof(Metronome));
            using (Stream s = File.Create($"saves/{name}.beat"))
            using (var w = XmlDictionaryWriter.CreateBinaryWriter(s))
            {
                ds.WriteObject(w, GetInstance());
            }
        }

        /** <summary>Load a previously saved beat by name.</summary>
         * <param name="fileName">The name of the beat to open.</param> */
        static public void Load(string fileName)
        {
            var ds = new DataContractSerializer(typeof(Metronome));
            using (Stream s = File.OpenRead($"saves/{fileName}.beat"))
            using (var w = XmlDictionaryReader.CreateBinaryReader(s, XmlDictionaryReaderQuotas.Max))
            {
                ds.ReadObject(w);
            }
        }

        /** <summary>Prepare to deserialize. Used in loading a saved beat.</summary> */
        [OnDeserializing]
        void BeforeDeserialization(StreamingContext sc)
        {
            Instance = this;
            Mixer = new MixingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(16000, 2));
            Player = new DirectSoundOut();
            Player.Init(Mixer);
        }

        /** <summary>After deserializing, add in the layers and audio sources.</summary> */
        [OnDeserialized]
        void Deserialized(StreamingContext sc)
        {
            foreach(Layer layer in Layers)
            {
                layer.Deserialize();
                AddSourcesFromLayer(layer);
            }
        }

        /** <summary>Dispose of resoures from all members.</summary> */
        public void Dispose()
        {
            Player.Dispose();
            Layers.ForEach((x) => x.Dispose());
        }
    }


    /** <summary>A layer representing a rhythmic pattern within the complete beat.</summary> */
    [DataContract]
    public class Layer : IDisposable
    {
        /** <summary>The individual beat cells contained by this layer.</summary> */
        public List<BeatCell> Beat;

        /** <summary>The audio sources that are not pitch are the base sound.</summary> */
        public Dictionary<string, IStreamProvider> AudioSources = new Dictionary<string, IStreamProvider>();
        /** <summary>The base audio source. Could be a pitch or wav file source.</summary> */
        public IStreamProvider BaseAudioSource;
        /** <summary>If this layer has any pitch sounds, they are held here.</summary> */
        public PitchStream BasePitchSource; // only use one pitch source per layer
        /** <summary>True if the base source is a pitch.</summary> */
        [DataMember]public bool IsPitch;
        /** <summary>The beat code string that was passed in to create the rhythm of this layer.</summary> */
        [DataMember]public string ParsedString;
        /** <summary>The fractional portion of sample per second values are accumulated here and added in when over 1.</summary> */
        [DataMember]public double Remainder = .0; // holds the accumulating fractional milliseconds.
        /** <summary>A value in quarter notes that all sounds in this layer are offset by.</summary> */
        [DataMember]public double Offset = 0; // in BPM
        /** <summary>The name of the base source.</summary> */
        [DataMember]protected string BaseSourceName;
        /** <summary>True if the layer is muted.</summary> */
        public bool IsMuted = false;
        /** <summary>True if the layer is part of the soloed group.</summary> */
        public bool IsSoloed = false;
        /** <summary>True if a solo group exists.</summary> */
        public static bool SoloGroupEngaged = false; // is there a solo group?
        /** <summary>Does the layer contain a hihat closed source?</summary> */
        public bool HasHiHatClosed = false;
        /** <summary>Does the layer contain a hihat open source?</summary> */
        public bool HasHiHatOpen = false;

        [DataMember]protected float volume;
        /** <summary>Set the volume of all sound sources in this layer.</summary> */
        public float Volume
        {
            get { return volume; }
            set
            {
                volume = value;
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

        [DataMember]protected float pan;
        /** <summary>Set the pan value for all sound sources in this layer.</summary> */
        public float Pan
        {
            get
            {
                return pan;
            }
            set
            {
                pan = value;
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

        /** <summary>Layer constructor</summary>
         * <param name="baseSourceName">Name of the base sound source.</param>
         * <param name="beat">Beat code.</param>
         * <param name="offset">Amount of offset</param>
         * <param name="pan">Set the pan</param>
         * <param name="volume">Set the volume</param> */
        public Layer(string beat, string baseSourceName, string offset = "", float pan = 0f, float volume = 1f)
        {
            SetBaseSource(baseSourceName);
            if (offset != "")
                SetOffset(offset);
            Parse(beat); // parse the beat code into this layer
            Volume = volume;
            if (pan != 0f)
                Pan = pan;
            Metronome.GetInstance().AddLayer(this);
        }

        /** <summary>Parse the beat code, generating beat cells.</summary>
         * <param name="beat">Beat code.</param> */
        public void Parse(string beat)
        {
            ParsedString = beat;
            // remove comments
            beat = Regex.Replace(beat, @"!.*?!", "");

            if (beat.Contains('$'))
            {
                // prep single cell repeat on ref if exists
                beat = Regex.Replace(beat, @"($[\ds]+)(\(\d\))", "[$1]$2");
                //resolve beat referencing
                while (beat.Contains('$'))
                {
                    string refBeat;
                    // is a self reference?
                    if (beat[beat.IndexOf('$') + 1].ToString().ToLower() == "s" ||
                        Regex.Match(beat, @"\$(\d+)").Groups[1].Value == (Metronome.GetInstance().Layers.Count + 1).ToString())
                    {
                        refBeat = Regex.Replace(ParsedString, @"!.*?!", "");
                    }
                    else
                    {
                        //get the index of the referenced beat, if exists
                        int refIndex = int.Parse(Regex.Match(beat, @"\$[\d]+").Value.Substring(1)) - 1;
                        // does referenced beat exist?
                        refIndex = Metronome.GetInstance().Layers.ElementAtOrDefault(refIndex) == null ? 0 : refIndex;
                        refBeat = Regex.Replace(Metronome.GetInstance().Layers[refIndex].ParsedString, @"!.*?!", "");

                        // remove sound source modifiers for non self references
                        refBeat = Regex.Replace(refBeat, @"@[a-gA-G]?[#b]?[\d.]+", "");
                    }
                    // remove references and their innermost nest from the referenced beat
                    while (refBeat.Contains('$'))
                    {
                        if (Regex.IsMatch(refBeat, @"[[{][^[{\]}]*\$[^[{\]}]*[\]}][^\]},]*"))
                            refBeat = Regex.Replace(refBeat, @"[[{][^[{\]}]*\$[^[{\]}]*[\]}][^\]},]*", "");
                        else
                            refBeat = Regex.Replace(refBeat, @"\$[\ds]+,?", ""); // straight up replace
                    }
                    // clean out empty cells
                    refBeat = Regex.Replace(refBeat, @",,", ",");
                    refBeat = Regex.Replace(refBeat, @",$", "");

                    // replace in the refBeat
                    var match = Regex.Match(beat, @"\$[\ds]+");
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
                string inner = Regex.Replace(match.Groups[1].Value, @"(?<!\]\d+)(?=([\],+-]|$))", "*" + match.Groups[2].Value);
                // switch the multiplier to be in front of pitch modifiers
                inner = Regex.Replace(inner, @"(@[a-gA-G]?#?\d+)(\*[\d.*/]+)", "$2$1");
                // insert into beat
                beat = beat.Substring(0, match.Index) + inner + beat.Substring(match.Index + match.Length);
            }

            // handle single cell repeats
            while (Regex.IsMatch(beat, @"[^\]]\(\d+\)"))
            {
                var match = Regex.Match(beat, @"([.\d+\-/*]+@?[a-gA-G]?#?)\((\d+)\)([\d\-+/*.]*)");
                StringBuilder result = new StringBuilder(beat.Substring(0, match.Index));
                for (int i = 0; i < int.Parse(match.Groups[2].Value); i++)
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
                var match = Regex.Match(beat, @"\[([^\][]+?)\]\(?(\d+)\)?([\d\-+/*.]*)");
                StringBuilder result = new StringBuilder();
                int itr = int.Parse(match.Groups[2].Value);
                for (int i = 0; i < itr; i++)
                {
                    // if theres a last time exit point, only copy up to that
                    if (i == itr - 1 && match.Value.Contains('|'))
                    {
                        result.Append(match.Groups[1].Value.Substring(0, match.Groups[1].Value.IndexOf('|')));
                    }
                    else result.Append(match.Groups[1].Value); // copy the group

                    if (i == itr - 1)
                    {
                        result.Append("+0").Append(match.Groups[3].Value);
                    }
                    else result.Append(",");
                }
                result.Replace('|', ',');
                beat = beat.Substring(0, match.Index) + result.Append(beat.Substring(match.Index + match.Length)).ToString();
            }

            // fix instances of a pitch modifier being following by +0 from repeater
            beat = Regex.Replace(beat, @"(@[a-gA-G]?[#b]?[\d.]+)(\+[\d.\-+/*]+)", "$2$1");
            
            BeatCell[] cells = beat.Split(',').Select((x) =>
            {
                var match = Regex.Match(x, @"([\d.+\-/*]+)@?(.*)");
                string source = match.Groups[2].Value;

                if (Regex.IsMatch(source, @"^[a-gA-G][#b]?\d{1,2}"))
                {
                    // is a pitch reference
                    return new BeatCell(match.Groups[1].Value, source);
                }
                else // ref is a plain number. use as pitch or wav file depending on base source.
                {
                    if (IsPitch)
                        return new BeatCell(match.Groups[1].Value, source);
                    else
                        return new BeatCell(match.Groups[1].Value, source != "" ? WavFileStream.FileNameIndex[int.Parse(source), 0] : "");
                }

            }).ToArray();

            SetBeat(cells);
        }

        /** <summary>Set the base source. Will also set Base pitch if a pitch.</summary>
         * <param name="baseSourceName">Name of source to use.</param> */
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

                if (BeatCell.HiHatOpenFileNames.Contains(baseSourceName)) HasHiHatOpen = true;
                else if (BeatCell.HiHatClosedFileNames.Contains(baseSourceName)) HasHiHatClosed = true;
            }
        }

        /** <summary>Set the offset for this layer.</summary>
         * <param name="offset">Quarter notes to offset by.</param> */
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

        /** <summary>Set the offset for this layer.</summary>
         * <param name="offset">Beat code value to offset by.</param> */
        public void SetOffset(string offset)
        {
            double os = BeatCell.Parse(offset);
            SetOffset(os);
        }

        /** <summary>Add array of beat cells and create all audio sources.</summary>
         * <param name="beat">Array of beat cells.</param> */
        public void SetBeat(BeatCell[] beat)
        {
            //float tempo = Metronome.GetInstance().Tempo;
            List<string> sources = new List<string>();

            for (int i = 0; i < beat.Count(); i++)
            {
                beat[i].Layer = this;
                if (beat[i].SourceName != string.Empty && beat[i].SourceName.Count() > 5)
                {
                    // should cells of the same source use the same audiosource instead of creating new source each time? Yes
                    if (!AudioSources.ContainsKey(beat[i].SourceName))
                    {
                        var wavStream = new WavFileStream(beat[i].SourceName);
                        wavStream.Layer = this;
                        AudioSources.Add(beat[i].SourceName, wavStream);
                    }
                    beat[i].AudioSource = AudioSources[beat[i].SourceName];

                    if (BeatCell.HiHatOpenFileNames.Contains(beat[i].SourceName)) HasHiHatOpen = true;
                    else if (BeatCell.HiHatClosedFileNames.Contains(beat[i].SourceName)) HasHiHatClosed = true;
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
                        else BasePitchSource.SetFrequency(beat[i].SourceName, beat[i]);
                        beat[i].AudioSource = BasePitchSource;
                    }
                    else
                    {
                        if (IsPitch)
                        {
                            // no pitch defined, use base pitch
                            BasePitchSource.SetFrequency(BasePitchSource.BaseFrequency.ToString(), beat[i]);
                        }
                        beat[i].AudioSource = BaseAudioSource;
                        // is hihat sound?
                        if (BeatCell.HiHatClosedFileNames.Contains(BaseSourceName)) beat[i].IsHiHatClosed = true;
                        else if (BeatCell.HiHatOpenFileNames.Contains(BaseSourceName)) beat[i].IsHiHatOpen = true;
                    }
                }
                // set beat's value based on tempo and bytes/sec
                beat[i].SetBeatValue();
            }

            Beat = beat.ToList();

            // match hihat close sounds to preceding hihat open sound
            if (Beat.Count(x => x.IsHiHatClosed) != 0 && Beat.Count(x => x.IsHiHatOpen) != 0)
            {
                for (int i=0; i<Beat.Count; i++)
                {
                    if (Beat[i].IsHiHatOpen)
                    {
                        double accumulator = 0;
                        // find nearest following close hihat sound.
                        for (int p=i+1; p!=i; p++)
                        {
                            if (p == Beat.Count) { p = -1; continue; } // back to start
            
                            if (Beat[p].IsHiHatClosed)
                            {
                                // set the duration for the open hihat sound.
                                Beat[i].hhDuration = accumulator + Beat[i].Bpm;
                                accumulator = 0;
                                break;
                            }
                            else accumulator += Beat[p].Bpm;
                        }
                    }
                }
            }

            SetBeatCollectionOnSources();
        }

        /** <summary>Set the beat collections for each sound source.</summary> */
        public void SetBeatCollectionOnSources()
        {
            List<IStreamProvider> completed = new List<IStreamProvider>();

            // for each beat, iterate over all beats and build a beat list of values from beats of same source.
            for (int i = 0; i < Beat.Count; i++)
            {
                List<double> cells = new List<double>();
                List<double> hhDurations = new List<double>(); // open hihat sound durations.
                double accumulator = 0;
                // Once per audio source
                if (completed.Contains(Beat[i].AudioSource)) continue;
                // if selected beat is not first in cycle, set it's offset
                if (i != 0)
                {
                    double offsetAccumulate = Offset;
                    for (int p = 0; p < i; p++)
                    {
                        offsetAccumulate += Beat[p].Bpm;
                    }

                    Beat[i].AudioSource.SetOffset(BeatCell.ConvertFromBpm(offsetAccumulate, Beat[i].AudioSource));
                }
                // iterate over beats starting with current one
                for (int p = i; ; p++)
                {

                    if (p == Beat.Count) p = 0;

                    if (Beat[p].AudioSource == Beat[i].AudioSource)
                    {

                        // add accumulator to previous element in list
                        if (cells.Count != 0)
                        {
                            cells[cells.Count - 1] += accumulator;
                            accumulator = 0f;
                        }
                        cells.Add(Beat[p].Bpm);
                        hhDurations.Add(Beat[p].hhDuration);
                    }
                    else accumulator += Beat[p].Bpm;

                    // job done if current beat is one before the outer beat.
                    if (p == i - 1 || (i == 0 && p == Beat.Count - 1))
                    {
                        cells[cells.Count - 1] += accumulator;
                        break;
                    }
                }
                completed.Add(Beat[i].AudioSource);

                Beat[i].AudioSource.BeatCollection = new SourceBeatCollection(this, cells.ToArray(), Beat[i].AudioSource, hhDurations[0]);
            }
        }

        /** <summary>When a close hihat interval occurs, push a false or a true if it was muted along with the new interval value.</summary> */
        public Stack<KeyValuePair<bool, double>> HiHatCloseIsMutedStack = new Stack<KeyValuePair<bool, double>>();

        /** <summary>Reset this layer so that it will play from the start.</summary> */
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

        /** <summary>Mute or unmute this layer.</summary> */
        public void ToggleMute()
        {
            IsMuted = !IsMuted;
        }

        /** <summary>Add to soloed group.</summary> */
        public void ToggleSoloGroup()
        {
            if (IsSoloed)
            {
                // unsolo and close the solo group if this was the only member
                IsSoloed = false;
                if (Metronome.GetInstance().Layers.Where(x => x.IsSoloed == true).Count() == 0)
                {
                    SoloGroupEngaged = false;
                }
            }
            else
            {
                // add this layer to solo group. all layers not in group will be muted.
                IsSoloed = true;
                SoloGroupEngaged = true;
            }
        }

        /** <summary>Create necessary components from the serialized values.</summary> */
        public void Deserialize()
        {
            AudioSources = new Dictionary<string, IStreamProvider>();
            SetBaseSource(BaseSourceName);
            Parse(ParsedString);
            if (Offset != 0)
                SetOffset(Offset);
            if (pan != 0)
                Pan = pan;
            Volume = volume;
        }

        public void Dispose()
        {
            foreach (IStreamProvider src in AudioSources.Values)
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
        public bool IsHiHatClosed = false;
        public bool IsHiHatOpen = false;
        public double hhDuration = 0; // if using hihat sounds, how long in BPM the open hihat sound should last.

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

        //public BeatCell(double beat, string sourceName = "") // value of beat, ex. "1/3"
        //{
        //    SourceName = sourceName;
        //    Bpm = beat;
        //}

        public BeatCell(string beat, string sourceName = "")
        {
            SourceName = sourceName;
            Bpm = Parse(beat);

            // is it a hihat closed or open sound?
            if (HiHatOpenFileNames.Contains(sourceName))
            {
                IsHiHatOpen = true;
            }
            else if (HiHatClosedFileNames.Contains(sourceName))
            {
                IsHiHatClosed = true;
            }
        }

        static public string[] HiHatOpenFileNames = new string[]
        {
            "wav/hihat_half_center_v4.wav",
            "wav/hihat_half_center_v7.wav",
            "wav/hihat_half_center_v10.wav",
            "wav/hihat_half_edge_v7.wav",
            "wav/hihat_half_edge_v10.wav",
            "wav/hihat_open_center_v4.wav",
            "wav/hihat_open_center_v7.wav",
            "wav/hihat_open_center_v10.wav",
            "wav/hihat_open_edge_v7.wav",
            "wav/hihat_open_edge_v10.wav"
        };

        static public string[] HiHatClosedFileNames = new string[]
        {
            "wav/hihat_pedal_v3.wav",
            "wav/hihat_pedal_v5.wav"
        };

        static public double Parse(string str)
        {
            string operators = "";
            for (int i = 0; i < str.Length; i++)
            {
                if (str[i] == '+' || str[i] == '-' || str[i] == '*' || str[i] == '/')
                    operators += str[i];
            }
            double[] numbers = str.Split(new char[] { '+', '-', '*', '/' }).Select((x) => Convert.ToDouble(x)).ToArray();

            // do mult and div
            while (operators.IndexOfAny(new[] { '*', '/' }) > -1)
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

            return numbers[0];
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

        double volume;
        public double Volume
        {
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
            //if (IsRandomMuted())
            //{
            //    //BeatCollection.Enumerator.MoveNext();
            //    //result += BeatCollection.Enumerator.Current;
            //    currentlyMuted = true;
            //}
            //else currentlyMuted = false;
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


    /** <summary>Handles the reading of .wav file sound sources.</summary> */
    public class WavFileStream : WaveStream, IStreamProvider
    {
        WaveFileReader sourceStream;

        /**<summary>This is added to the mixer. Exposes controls for volume and pan.</summary>*/
        public WaveChannel32 Channel { get; set; }

        public bool IsPitch { get { return false; } }

        /**<summary>The layer that this sound is associated with</summary>*/
        public Layer Layer { get; set; }

        /**<summary>Holds the byte interval values and HiHat duration for HH open sounds.</summary>*/
        public SourceBeatCollection BeatCollection { get; set; }

        /**<summary>The byte rate for this stream.</summary>*/
        public int BytesPerSec { get; set; }

        /**<summary>The cached sound source to read from.</summary>*/
        byte[] cache;
        /**<summary>Current position in the cached sound source byte array.</summary>*/
        int cacheIndex = 0;

        /**<summary>Constructor</summary>*/
        public WavFileStream(string fileName)
        {
            if (fileName == "silentbeat")
            {
                sourceStream = new WaveFileReader(FileNameIndex[1,0]);
            }
            else
            {
                sourceStream = new WaveFileReader(fileName);
            }
            BytesPerSec = sourceStream.WaveFormat.AverageBytesPerSecond;
            Channel = new WaveChannel32(this);

            // check if in cache store
            if (CachedStreams.ContainsKey(fileName)) {
                cache = CachedStreams[fileName];
            }
            else // add to cache
            {
                MemoryStream ms = new MemoryStream();
                sourceStream.CopyTo(ms);
                cache = ms.GetBuffer();
                CachedStreams.Add(fileName, cache);
                ms.Dispose();
            }
            //memStream = new MemoryStream(cache);

            Metronome met = Metronome.GetInstance();
            // set audible/silent interval if already exists
            if (met.IsSilentInterval)
                SetSilentInterval(met.AudibleInterval, met.SilentInterval);

            // determine if first sound will be muted
            if (met.IsRandomMute || met.IsSilentInterval)
            {
                SetInitialMuting();
            }
            
            // is this a hihat sound?
            if (BeatCell.HiHatOpenFileNames.Contains(fileName)) IsHiHatOpen = true;
            else if (BeatCell.HiHatClosedFileNames.Contains(fileName)) IsHiHatClose = true;
        }

        /**<summary>The volume for this sound source.</summary>*/
        public double Volume
        {
            get { return Channel.Volume; }
            set { Channel.Volume = (float)value; }
        }

        /**<summary>The pan control for this sound. -1 to 1</summary>*/
        public float Pan
        {
            get { return Channel.Pan; }
            set { Channel.Pan = value; }
        }

        public override WaveFormat WaveFormat
        {
            get { return sourceStream.WaveFormat; }
        }

        /**<summary>Reset this sound so that it will play from the start.</summary>*/
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
            // TODO: hihat open settings
            HiHatOpenIsMuted = false;
            HiHatMuteInitiated = false;
            HiHatCycleToMute = 0;
            cycle = 0;

            // will first muting occur for first sound?
            SetInitialMuting();

            //memStream.Position = 0;
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

            previousByteInterval = result;

            if (IsSilentIntervalSilent())
            {
                return result;
            }
            
            currentlyMuted = IsRandomMuted();
            
            return result;
        }

        public void MultiplyByteInterval()
        {
            if (intervalMultiplyCued)
            {
                double div = ByteInterval / 4;
                div *= intervalMultiplyFactor;
                Layer.Remainder += div - (int)div;
                ByteInterval = (int)div * 4;

                // multiply the offset aswell
                if (hasOffset)
                {
                    div = initialOffset / 4;
                    div *= intervalMultiplyFactor;
                    offsetRemainder += div - (int)div;
                    initialOffset = (int)div * 4;
                }

                // do the hihat cutoff interval
                if (IsHiHatOpen && BeatCollection.CurrentHiHatDuration != null && BeatCollection.CurrentHiHatDuration != 0)
                {
                    div = (int)BeatCollection.CurrentHiHatDuration / 4;
                    BeatCollection.CurrentHiHatDuration = (int)(div * intervalMultiplyFactor) * 4;
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

        public void SetInitialMuting()
        {
            if (ByteInterval == 0)
            {
                cacheIndex = cache.Length;
                currentlyMuted = IsRandomMuted();
                silentIntvlSilent = IsSilentIntervalSilent();

                // prevents open sound getting chopped if closed sound occurs before.
                if (IsHiHatOpen && Layer.HasHiHatClosed) HiHatMuteInitiated = true;
            }
        }

        protected bool IsSilentIntervalSilent() // check if silent interval is currently silent or audible. Perform timing shifts
        {
            if (!Metronome.GetInstance().IsSilentInterval) return false;

            //currentSlntIntvl -= previousByteInterval;
            currentSlntIntvl -= ByteInterval + initialOffset;
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
            bool result;
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
            {
                result = rand < Metronome.GetInstance().RandomMutePercent;
            }
            else
            {
                // countdown
                if (randomMuteCountdown > 0) randomMuteCountdown -= ByteInterval + initialOffset; //previousByteInterval;
                if (randomMuteCountdown < 0) randomMuteCountdown = 0;

                float factor = (float)(randomMuteCountdownTotal - randomMuteCountdown) / randomMuteCountdownTotal;
                result = rand < Metronome.GetInstance().RandomMutePercent * factor;
            }

            //if (IsHiHatClose && Layer.HasHiHatOpen && result)
            //{
            //    foreach (IStreamProvider sp in Layer.AudioSources.Values)
            //    {
            //        if (!sp.IsPitch)
            //        {
            //            WavFileStream wfs = sp as WavFileStream;
            //            if (wfs.IsHiHatOpen)
            //            {
            //                wfs.BeatCollection.CurrentHiHatDuration += BeatCollection.Enumerator.Current;
            //            }
            //        }
            //    }
            //}

            return result;
        }

        public void SetOffset(double value)
        {
            offsetRemainder = ((int)value) - value;
            initialOffset = ((int)value) * 4;
            hasOffset = true;

            // is first sound muted?
            SetInitialMuting();
        }

        public double GetOffset()
        {
            return initialOffset + offsetRemainder;
        }

        protected int initialOffset = 0;
        protected double offsetRemainder = 0;
        protected bool hasOffset = false;

        protected double SilentInterval; // remaining samples in silent interval
        protected double AudibleInterval; // remaining samples in audible interval
        protected int currentSlntIntvl;
        protected bool silentIntvlSilent = false;
        protected double SilentIntervalRemainder; // fractional portion
        protected bool IsHiHatOpen = false; // is this an open hihat sound?
        protected bool IsHiHatClose = false; // is this a close hihat sound?
        protected bool HiHatOpenIsMuted = false; // an open hihat sound was muted so currenthihatduration should not be increased by closed sounds being muted.

        public void SetSilentInterval(double audible, double silent)
        {
            AudibleInterval = BeatCell.ConvertFromBpm(audible, this) * 4;
            SilentInterval = BeatCell.ConvertFromBpm(silent, this) * 4;
            currentSlntIntvl = (int)AudibleInterval - initialOffset - 4;
            SilentIntervalRemainder = audible - currentSlntIntvl + offsetRemainder;
        }

        protected int previousByteInterval = 0;

        public int ByteInterval;

        public int HiHatCycleToMute;
        public int HiHatByteToMute;
        bool HiHatMuteInitiated = false;
        int cycle = 0;

        public override int Read(byte[] buffer, int offset, int count)
        {
            int bytesCopied = 0;
            int cacheSize = cache.Length;
        
            // perform interval multiplication if cued
            if (intervalMultiplyCued)
            {
                MultiplyByteInterval();
            }

            // set the upcoming hihat close time for hihat open sounds
            if (!hasOffset && IsHiHatOpen && cycle == HiHatCycleToMute - 1 && !HiHatMuteInitiated)
            {
                BeatCollection.CurrentHiHatDuration = HiHatByteToMute + count;
                HiHatMuteInitiated = true;
            }

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
                    continue;
                }
        
                if (ByteInterval == 0)
                {        
                    if (!silentIntvlSilent && !currentlyMuted)
                    {
                        if (IsHiHatOpen)
                        {
                            HiHatOpenIsMuted = false;
                            HiHatMuteInitiated = false;
                        }
                        cacheIndex = 0;
                    }

                    ByteInterval = GetNextInterval();
        
                    // if this is a hihat down, pass it's time position to all hihat opens in this layer
                    if (IsHiHatClose && Layer.HasHiHatOpen && !silentIntvlSilent && !currentlyMuted)
                    {
                        int total = bytesCopied + ByteInterval + offset;
                        int cycles = total / count + cycle;
                        int bytes = total % count;

                        // assign the hihat cutoff to all open hihat sounds.
                        IEnumerable hhos = Layer.AudioSources.Where(x => BeatCell.HiHatOpenFileNames.Contains(x.Key)).Select(x => x.Value);
                        foreach (WavFileStream hho in hhos)
                        {
                            hho.HiHatByteToMute = bytes;
                            hho.HiHatCycleToMute = cycles;
                        }
                    }
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
                        if (!currentlyMuted)
                            cacheIndex = 0;
                    }
        
                    // dont read more than cache size
                    if (chunkSize > cacheSize - cacheIndex)
                    {
                        chunkSize = cacheSize - cacheIndex;
                    }
        
                    // if hihat open sound, account for duration
                    if (IsHiHatOpen && BeatCollection.CurrentHiHatDuration > 0)
                    {
                        if (chunkSize >= BeatCollection.CurrentHiHatDuration)
                        {
                            chunkSize = (int)BeatCollection.CurrentHiHatDuration;
                            
                        }
                        else BeatCollection.CurrentHiHatDuration -= chunkSize;

                    }
        
                    if (chunkSize >= 4)
                    {
                        // check for muting
                        if (Layer.IsMuted || (Pronome.Layer.SoloGroupEngaged && !Layer.IsSoloed) || HiHatOpenIsMuted)
                        {
                            Array.Copy(new byte[buffer.Length], 0, buffer, offset + bytesCopied, chunkSize); // muted
                        }
                        else
                        {
                            Array.Copy(cache, cacheIndex, buffer, offset + bytesCopied, chunkSize);
                            cacheIndex += chunkSize;
                        }
        
                        if (IsHiHatOpen && BeatCollection.CurrentHiHatDuration == chunkSize)
                        {
                            HiHatOpenIsMuted = true;
                            BeatCollection.CurrentHiHatDuration = 0;
                        }
                    }
                    bytesCopied += chunkSize;
                    ByteInterval -= chunkSize;
                }
                else // silence
                {
                    int smallest = Math.Min(ByteInterval, count - bytesCopied);
        
                    Array.Copy(new byte[smallest], 0, buffer, offset + bytesCopied, smallest);
        
                    ByteInterval -= smallest;
                    bytesCopied += smallest;
                }
            }

            if (IsHiHatOpen || IsHiHatClose) cycle++;
            return count;
        }

        static public string GetFileByName(string name)
        {
            int length = FileNameIndex.Length;
            string[] flat = new string[length];
            flat = FileNameIndex.Cast<string>().ToArray();
            return flat[Array.IndexOf(flat, name) - 1];
        }

        // store streams that have been cached in here
        static protected Dictionary<string, byte[]> CachedStreams = new Dictionary<string, byte[]>() { { "silentbeat", new byte[4] } };

        static public string[,] FileNameIndex = new string[,]
        {
            { "silentbeat", "silentbeat" }, // is cached as an empty sample
            { "wav/crash1_edge_v5.wav", "Crash Edge V1" },                        //1
            { "wav/crash1_edge_v8.wav", "Crash Edge V2" },                        //2
            { "wav/crash1_edge_v10.wav", "Crash Edge V3" },                       //3
            { "wav/floortom_v6.wav", "FloorTom V1" },                             //4
            { "wav/floortom_v11.wav", "FloorTom V2" },                            //5
            { "wav/floortom_v16.wav", "FloorTom V3" },                            //6
            { "wav/hihat_closed_center_v4.wav", "HiHat Closed Center V1" },       //7
            { "wav/hihat_closed_center_v7.wav", "HiHat Closed Center V2" },       //8
            { "wav/hihat_closed_center_v10.wav", "HiHat Closed Center V3" },      //9
            { "wav/hihat_closed_edge_v7.wav", "HiHat Closed Edge V1" },           //10
            { "wav/hihat_closed_edge_v10.wav", "HiHat Closed Edge V2" },          //11
            { "wav/hihat_half_center_v4.wav", "HiHat Half Center V1" },           //12
            { "wav/hihat_half_center_v7.wav", "HiHat Half Center V2" },           //13
            { "wav/hihat_half_center_v10.wav", "HiHat Half Center V3" },          //14
            { "wav/hihat_half_edge_v7.wav", "HiHat Half Edge V1" },               //15
            { "wav/hihat_half_edge_v10.wav", "HiHat Half Edge V2" },              //16
            { "wav/hihat_open_center_v4.wav", "HiHat Open Center V1" },           //17
            { "wav/hihat_open_center_v7.wav", "HiHat Open Center V2" },           //18
            { "wav/hihat_open_center_v10.wav", "HiHat Open Center V3" },          //19
            { "wav/hihat_open_edge_v7.wav", "HiHat Open Edge V1" },               //20
            { "wav/hihat_open_edge_v10.wav", "HiHat Open Edge V2" },              //21
            { "wav/hihat_pedal_v3.wav", "HiHat Pedal V1" },                       //22
            { "wav/hihat_pedal_v5.wav", "HiHat Pedal V2" },                       //23
            { "wav/kick_v7.wav", "Kick Drum V1" },                                //24
            { "wav/kick_v11.wav", "Kick Drum V2" },                               //25
            { "wav/kick_v16.wav", "Kick Drum V3" },                               //26
            { "wav/racktom_v6.wav", "RackTom V1" },                               //27
            { "wav/racktom_v11.wav", "RackTom V2" },                              //28
            { "wav/racktom_v16.wav", "RackTom V3" },                              //29
            { "wav/ride_bell_v5.wav", "Ride Bell V1" },                           //30
            { "wav/ride_bell_v8.wav", "Ride Bell V2" },                           //31
            { "wav/ride_bell_v10.wav", "Ride Bell V3" },                          //32
            { "wav/ride_center_v5.wav", "Ride Center V1" },                       //33
            { "wav/ride_center_v6.wav", "Ride Center V2" },                       //34
            { "wav/ride_center_v8.wav", "Ride Center V3" },                       //35
            { "wav/ride_center_v10.wav", "Ride Center V4" },                      //36
            { "wav/ride_edge_v4.wav", "Ride Edge V1" },                           //37
            { "wav/ride_edge_v7.wav", "Ride Edge V2" },                           //38
            { "wav/ride_edge_v10.wav", "Ride Edge V3" },                          //39
            { "wav/snare_center_v6.wav", "Snare Center V1" },                     //40
            { "wav/snare_center_v11.wav", "Snare Center V2" },                    //41
            { "wav/snare_center_v16.wav", "Snare Center V3" },                    //42
            { "wav/snare_edge_v6.wav", "Snare Edge V1" },                         //43
            { "wav/snare_edge_v11.wav", "Snare Edge V2" },                        //44
            { "wav/snare_edge_v16.wav", "Snare Edge V3" },                        //45
            { "wav/snare_rim_v6.wav", "Snare Rim V1" },                           //46
            { "wav/snare_rim_v11.wav", "Snare Rim V2" },                          //47
            { "wav/snare_rim_v16.wav", "Snare Rim V3" },                          //48
            { "wav/snare_xstick_v6.wav", "Snare XStick V1" },                     //49
            { "wav/snare_xstick_v11.wav", "Snare XStick V2" },                    //50
            { "wav/snare_xstick_v16.wav", "Snare XStick V3" },                    //51
        };

        void IStreamProvider.Dispose()
        {
            //memStream.Dispose();
            sourceStream.Dispose();
            Channel.Dispose();
            Dispose();
        }
    }


    public interface IStreamProvider
    {
        bool IsPitch { get; }

        int GetNextInterval();

        double Volume { get; set; }

        /**<summary>The pan setting for this sound source. -1 to 1.</summary>*/
        float Pan { get; set; }

        double Frequency { get; set; }

        void Dispose();

        void Reset();

        int BytesPerSec { get; set; }

        void SetOffset(double value);

        double GetOffset();

        void SetSilentInterval(double audible, double silent);

        void MultiplyByteInterval(double factor);

        Layer Layer { get; set; }

        SourceBeatCollection BeatCollection { get; set; }

        WaveFormat WaveFormat { get; }
    }


    public class SourceBeatCollection : IEnumerable<int>
    {
        Layer Layer;
        double[] Beats;
        public int? CurrentHiHatDuration = null;
        public IEnumerator<int> Enumerator;
        bool isWav;

        public SourceBeatCollection(Layer layer, double[] beats, IStreamProvider src, double hhDuration)
        {
            Layer = layer;
            Beats = beats.Select((x) => BeatCell.ConvertFromBpm(x, src)).ToArray();
            Enumerator = GetEnumerator();
            isWav = src.WaveFormat.AverageBytesPerSecond == 64000;
            //if (hhDurations.Where(x => x != 0).Count() > 0)
            //{ // has hihat durations.
            //    HHDuations = hhDurations.Select(x => BeatCell.ConvertFromBpm(x, src)).ToArray();
            //    hasHHDurations = true;
            //}
            if (hhDuration != 0)
                CurrentHiHatDuration = (int)hhDuration * 4;
        }

        public IEnumerator<int> GetEnumerator()
        {
            for (int i = 0; ; i++)
            {
                if (i == Beats.Count()) i = 0; // loop over collection

                //if (hasHHDurations)
                //{
                //    CurrentHiHatDuration = (int)HHDuations[i] * 4;
                //
                //    if (CurrentHiHatDuration == 0) CurrentHiHatDuration = null;
                //}

                double bpm = Beats[i];//BeatCell.ConvertFromBpm(Beats[i], BytesPerSec);
                
                int whole = (int)bpm;

                Layer.Remainder += bpm - whole; // add to layer's remainder accumulator

                while (Layer.Remainder >= 1) // fractional value exceeds 1, add it to whole
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

        public void MultiplyBeatValues(double factor)
        {
            Beats = Beats.Select(x => x * factor).ToArray();
        }
    }
}