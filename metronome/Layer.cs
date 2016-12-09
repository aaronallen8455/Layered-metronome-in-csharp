using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Runtime.Serialization;

namespace Pronome
{
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
        [DataMember]
        public bool IsPitch;
        /** <summary>The beat code string that was passed in to create the rhythm of this layer.</summary> */
        [DataMember]
        public string ParsedString;
        /** <summary>The fractional portion of sample per second values are accumulated here and added in when over 1.</summary> */
        [DataMember]
        public double Remainder = .0; // holds the accumulating fractional milliseconds.
        /** <summary>A value in quarter notes that all sounds in this layer are offset by.</summary> */
        [DataMember]
        public double Offset = 0; // in BPM
        /** <summary>The name of the base source.</summary> */
        [DataMember]
        protected string BaseSourceName;
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

        [DataMember]
        protected float volume;
        /** <summary>Set the volume of all sound sources in this layer.</summary> */
        public float Volume
        {
            get { return volume; }
            set
            {
                volume = value;
                foreach (IStreamProvider src in AudioSources.Values) src.Volume = value;
                if (BasePitchSource != null) BasePitchSource.Volume = value;
            }
        }

        [DataMember]
        protected float pan;
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
                if (BasePitchSource != null) BasePitchSource.Pan = value;
            }
        }

        /** <summary>Layer constructor</summary>
         * <param name="baseSourceName">Name of the base sound source.</param>
         * <param name="beat">Beat code.</param>
         * <param name="offset">Amount of offset</param>
         * <param name="pan">Set the pan</param>
         * <param name="volume">Set the volume</param> */
        public Layer(string beat, string baseSourceName = null, string offset = "", float pan = 0f, float volume = 1f)
        {
            if (baseSourceName == null) // auto generate a pitch if no source is specified
            {
                SetBaseSource(GetAutoPitch());
            }
            else
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

                        // remove sound source modifiers for non self references, unless its @0
                        refBeat = Regex.Replace(refBeat, @"@[a-gA-G]?[#b]?[1-9.]+", "");
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
                string inner = Regex.Replace(match.Groups[1].Value, @"(?<!\]\d*)(?=([\]\(\|,+-]|$))", "*" + match.Groups[2].Value);
                // switch the multiplier to be in front of pitch modifiers
                inner = Regex.Replace(inner, @"(@[a-gA-G]?[#b]?\d+)(\*[\d.*/]+)", "$2$1");
                // insert into beat
                beat = beat.Substring(0, match.Index) + inner + beat.Substring(match.Index + match.Length);
            }

            // handle single cell repeats
            while (Regex.IsMatch(beat, @"[^\]]\(\d+\)"))
            {
                var match = Regex.Match(beat, @"([.\d+\-/*]+@?[a-gA-G]?[#b]?\d*)\((\d+)\)([\d\-+/*.]*)");
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
            // remove baes source from AudioSources if exists
            if (!IsPitch && BaseSourceName != null) AudioSources.Remove(BaseSourceName); // for pitch layers, base source is not in AudioSources.

            // is sample or pitch source?
            if (baseSourceName.Count() <= 5)
            {
                BasePitchSource = new PitchStream()
                {
                    BaseFrequency = PitchStream.ConvertFromSymbol(baseSourceName),
                    Layer = this
                };
                BaseAudioSource = BasePitchSource; // needs to be cast back to ISampleProvider when added to mixer
                IsPitch = true;
            }
            else
            {
                BaseAudioSource = new WavFileStream(baseSourceName)
                {
                    Layer = this
                };
                AudioSources.Add(baseSourceName, BaseAudioSource);
                IsPitch = false;

                if (BeatCell.HiHatOpenFileNames.Contains(baseSourceName)) HasHiHatOpen = true;
                else if (BeatCell.HiHatClosedFileNames.Contains(baseSourceName)) HasHiHatClosed = true;
            }

            // reassign source to existing cells that use the base source.
            if (Beat != null)
            {
                var baseBeats = Beat.Where(x => x.SourceName == BaseSourceName).ToList();
                SetBeatCollectionOnSources(baseBeats);

                // reasses the hihat status of base source cells
                if (BeatCell.HiHatClosedFileNames.Contains(baseSourceName))
                    baseBeats.ForEach(x => x.IsHiHatClosed = true);
                else
                    baseBeats.ForEach(x => x.IsHiHatClosed = false);
                if (BeatCell.HiHatOpenFileNames.Contains(baseSourceName))
                    baseBeats.ForEach(x => x.IsHiHatOpen = true);
                else
                    baseBeats.ForEach(x => x.IsHiHatOpen = false);

                // reasses layer hihat status
                HasHiHatOpen = Beat.Any(x => BeatCell.HiHatOpenFileNames.Contains(x.SourceName));
                HasHiHatClosed = Beat.Any(x => BeatCell.HiHatClosedFileNames.Contains(x.SourceName));
            }

            BaseSourceName = baseSourceName;
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

            //// set for pitch / base source

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
                        var wavStream = new WavFileStream(beat[i].SourceName)
                        {
                            Layer = this
                        };
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

            SetBeatCollectionOnSources(Beat);
        }

        /** <summary>Set the beat collections for each sound source.</summary> 
         * <param name="Beat">The cells to process</param>
         */
        public void SetBeatCollectionOnSources(List<BeatCell> Beat)
        {
            List<IStreamProvider> completed = new List<IStreamProvider>();

            // for each beat, iterate over all beats and build a beat list of values from beats of same source.
            for (int i = 0; i < Beat.Count; i++)
            {
                List<double> cells = new List<double>();
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
                // iterate over beats starting with current one. Aggregate with cells that have the same audio source.
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

                Beat[i].AudioSource.BeatCollection = new SourceBeatCollection(this, cells.ToArray(), Beat[i].AudioSource);
            }

            // do any initial muting, includes hihat timings
            AudioSources.Values.ToList().ForEach(x => x.SetInitialMuting());
        }

        /**<summary>Get a random pitch based on existing pitch layers</summary>*/
        public string GetAutoPitch()
        {
            string note;
            byte octave;

            string[] noteNames =
            {
                "A", "A#", "B", "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#"
            };

            ushort[] intervals = { 3, 4, 5, 7, 8, 9 };

            do
            {
                // determine the octave
                octave = Metronome.GetRandomNum() > 49 ? (byte)5 : (byte)4;
                // 80% chance to make a sonorous interval with last pitch layer
                if (Metronome.GetRandomNum() < 80)
                {
                    var last = Metronome.GetInstance().Layers.Last(x => IsPitch);
                    int index = Array.IndexOf(noteNames, last.BaseSourceName.TakeWhile(x => !char.IsNumber(x)));
                    index += intervals[Metronome.GetRandomNum() / (100 / 6)];
                    if (index > 11) index -= 12;
                    note = noteNames[index];
                }
                else
                {
                    // randomly pick note
                    note = noteNames[Metronome.GetRandomNum() / (100 / 12)];
                }
            }
            while (Metronome.GetInstance().Layers.Where(x => x.IsPitch).Any(x => x.BaseSourceName == note + octave));

            return note + octave;
        }

        /**<summary>Sum up all the Bpm values for beat cells.</summary>*/
        public double GetTotalBpmValue()
        {
            return Beat.Select(x => x.Bpm).Sum();
        }

        /** <summary>Reset this layer so that it will play from the start.</summary> */
        public void Reset()
        {
            Remainder = 0;
            foreach (IStreamProvider src in AudioSources.Values)
            {
                src.Reset();
            }
            //BaseAudioSource.Reset();
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

            if (BasePitchSource != null)
                BasePitchSource.Dispose();
        }
    }
}
