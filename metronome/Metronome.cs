﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Runtime.Serialization;
using System.Xml;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Pronome
{
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

        /** <summary>Used for recording to a wav file.</summary>*/
        protected WaveFileWriter Writer;

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

            if (layer.BasePitchSource != null) // if base source is a pitch stream.
                Mixer.AddMixerInput(layer.BasePitchSource);
            
            // transfer silent interval if exists
            if (IsSilentInterval)
            {
                foreach (IStreamProvider src in layer.AudioSources.Values)
                {
                    src.SetSilentInterval(AudibleInterval, SilentInterval);
                }

                if (layer.BasePitchSource != default(PitchStream))
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

        /** <summary>Record the beat to a wav file.</summary>
         * <param name="seconds">Number of seconds to record</param>
         * <param name="fileName">Name of file to record to</param>
         */
        public void Record(float seconds, string fileName)
        {
            if (fileName.Substring(-4).ToLower() != ".wav") // append wav extension
                fileName += ".wav";
            Writer = new WaveFileWriter(fileName, Mixer.WaveFormat);

            int bytesToRec = (int)(Mixer.WaveFormat.AverageBytesPerSecond / 4 * seconds);
            // align bytes
            bytesToRec -= bytesToRec % 4;
            float[] buffer = new float[bytesToRec];
            Mixer.Read(buffer, 0, bytesToRec);
            Writer.WriteSamples(buffer, 0, bytesToRec);
            Writer.Dispose();
            buffer = null;
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
                    if (x.BasePitchSource != null)
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
        [DataMember]
        public bool IsRandomMute = false;

        /** <summary>Percent chance that a note gets muted.</summary> */
        [DataMember]
        public int RandomMutePercent;
        /** <summary>Number of seconds over which the random mute percent ramps up to full value.</summary> */
        [DataMember]
        public int RandomMuteSeconds = 0;

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
                select n.AudioSources.Values
                into s
                from aud in s
                where !aud.IsPitch
                select aud;

            foreach (WavFileStream wfs in WavLayers)
            {
                wfs.SetInitialMuting();
            }
        }

        /** <summary>True if a silent interval is set.</summary> */
        [DataMember]
        public bool IsSilentInterval = false;

        /** <summary>The value in quarter notes that a beat plays audibly.</summary> */
        [DataMember]
        public double AudibleInterval;
        /** <summary>The value in quarter notes that a beat is silenced.</summary> */
        [DataMember]
        public double SilentInterval;

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

                    if (layer.BasePitchSource != default(PitchStream))
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
            foreach (Layer layer in Layers)
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
}
