using System;
using System.Linq;

namespace Pronome
{
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
}
