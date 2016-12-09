using System.Collections.Generic;
using System.Collections;
using System.Linq;

namespace Pronome
{
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
            for (int i = 0; ; i++)
            {
                if (i == Beats.Count()) i = 0; // loop over collection

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

        double _factor;
        public void MultiplyBeatValues(double factor)
        {
            _factor = factor;
        }
        public void MultiplyBeatValues()
        {
            if (_factor > 0)
                Beats = Beats.Select(x => x * _factor).ToArray();
        }
    }
}
