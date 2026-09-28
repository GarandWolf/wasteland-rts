using System;

namespace Wasteland.Sim.Movement
{
    /// <summary>Min-heap of (f, tie, index) for deterministic A*.</summary>
    internal sealed class MinHeap
    {
        private long[] _f;
        private int[] _tie;
        private int[] _index;
        private int _count;

        public MinHeap(int capacity)
        {
            int cap = System.Math.Max(capacity, 8);
            _f = new long[cap];
            _tie = new int[cap];
            _index = new int[cap];
        }

        public int Count
        {
            get { return _count; }
        }

        public void Clear()
        {
            _count = 0;
        }

        public void Push(long f, int tie, int index)
        {
            if (_count == _f.Length)
            {
                Grow();
            }

            int i = _count++;
            _f[i] = f;
            _tie[i] = tie;
            _index[i] = index;
            SiftUp(i);
        }

        public bool TryPop(out int index, out long f)
        {
            if (_count == 0)
            {
                index = 0;
                f = 0;
                return false;
            }

            index = _index[0];
            f = _f[0];
            _count--;
            if (_count > 0)
            {
                _f[0] = _f[_count];
                _tie[0] = _tie[_count];
                _index[0] = _index[_count];
                SiftDown(0);
            }

            return true;
        }

        private void Grow()
        {
            int cap = _f.Length * 2;
            long[] nf = new long[cap];
            int[] nt = new int[cap];
            int[] ni = new int[cap];
            Array.Copy(_f, 0, nf, 0, _count);
            Array.Copy(_tie, 0, nt, 0, _count);
            Array.Copy(_index, 0, ni, 0, _count);
            _f = nf;
            _tie = nt;
            _index = ni;
        }

        private void SiftUp(int i)
        {
            while (i > 0)
            {
                int p = (i - 1) >> 1;
                if (!Less(i, p))
                {
                    break;
                }

                Swap(i, p);
                i = p;
            }
        }

        private void SiftDown(int i)
        {
            while (true)
            {
                int l = (i << 1) + 1;
                if (l >= _count)
                {
                    break;
                }

                int smallest = l;
                int r = l + 1;
                if (r < _count && Less(r, l))
                {
                    smallest = r;
                }

                if (!Less(smallest, i))
                {
                    break;
                }

                Swap(i, smallest);
                i = smallest;
            }
        }

        private bool Less(int a, int b)
        {
            long fa = _f[a];
            long fb = _f[b];
            if (fa != fb)
            {
                return fa < fb;
            }

            int ta = _tie[a];
            int tb = _tie[b];
            if (ta != tb)
            {
                return ta < tb;
            }

            return _index[a] < _index[b];
        }

        private void Swap(int a, int b)
        {
            long f = _f[a];
            _f[a] = _f[b];
            _f[b] = f;
            int t = _tie[a];
            _tie[a] = _tie[b];
            _tie[b] = t;
            int idx = _index[a];
            _index[a] = _index[b];
            _index[b] = idx;
        }
    }
}
