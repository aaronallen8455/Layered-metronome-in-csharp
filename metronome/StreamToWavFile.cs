using System;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Pronome
{
    /**<summary>Wraps the mixer allowing playback to be written to a wav file.</summary>*/
    public class StreamToWavFile : ISampleProvider, IDisposable
    {
        protected MixingSampleProvider _mixer;

        protected WaveFileWriter _writer;

        public bool IsRecording = false;

        public WaveFormat WaveFormat { get; private set; }

        public StreamToWavFile(MixingSampleProvider mixer)
        {
            _mixer = mixer;
            WaveFormat = mixer.WaveFormat;
            //_writer = new WaveFileWriter("test.wav", WaveFormat);
        }

        public void InitRecording(string fileName)
        {
            if (!IsRecording)
            {
                if (fileName.Substring(fileName.Length - 4).ToLower() != ".wav") // append wav extension
                    fileName += ".wav";
                _writer = new WaveFileWriter(fileName, WaveFormat);
                IsRecording = true;
            }
        }

        public void Stop()
        {
            if (IsRecording)
            {
                _writer?.Dispose();
                IsRecording = false;
            }
        }

        public int Read(float[] buffer, int offset, int count)
        {
            int result = 0;
            try
            {
                result = _mixer.Read(buffer, offset, count);

                if (count > 0 && IsRecording)
                {
                    //write samples to file
                    _writer.WriteSamples(buffer, offset, count);
                }
            }
            catch (NullReferenceException) { }

            if (count == 0)
            {
                Dispose();
            }

            return result;
        }

        public void Dispose()
        {
            IsRecording = false;
            _writer?.Dispose();
        }
    }
}
