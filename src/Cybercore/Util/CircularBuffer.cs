using System;
using System.Collections;
using System.Collections.Generic;

//----------------------------------------------------------------------------
// "THE BEER-WARE LICENSE" (Revision 42):
// Joao Portela wrote this file. As long as you retain this notice you
// can do whatever you want with this stuff.If we meet some day, and you think
// this stuff is worth it, you can buy me a beer in return.
// Joao Portela
//----------------------------------------------------------------------------

namespace Cybercore.Util
{
    public class CircularBuffer<T> : IEnumerable<T>
    {
        public CircularBuffer(int capacity)
            : this(capacity, new T[] { })
        {
        }

        public CircularBuffer(int capacity, T[] items)
        {
            if (capacity < 1)
                throw new ArgumentException(
                    "Circular buffer cannot have negative or zero capacity.", nameof(capacity));
            if (items == null)
                throw new ArgumentNullException(nameof(items));
            if (items.Length > capacity)
                throw new ArgumentException(
                    "Too many items to fit circular buffer", nameof(items));

            buffer = new T[capacity];

            Array.Copy(items, buffer, items.Length);
            size = items.Length;

            start = 0;
            end = size == capacity ? 0 : size;
        }

        private readonly T[] buffer;
        private int end;
        private int size;
        private int start;

        public int Capacity => buffer.Length;
        public bool IsFull => Size == Capacity;
        public bool IsEmpty => Size == 0;
        public int Size => size;

        public T this[int index]
        {
            get
            {
                if (IsEmpty)
                    throw new IndexOutOfRangeException(
                        string.Format("Cannot access index {0}. Buffer is empty", index));
                if (index >= size)
                    throw new IndexOutOfRangeException(
                        string.Format("Cannot access index {0}. Buffer size is {1}", index, size));
                var actualIndex = InternalIndex(index);
                return buffer[actualIndex];
            }
            set
            {
                if (IsEmpty)
                    throw new IndexOutOfRangeException(
                        string.Format("Cannot access index {0}. Buffer is empty", index));
                if (index >= size)
                    throw new IndexOutOfRangeException(
                        string.Format("Cannot access index {0}. Buffer size is {1}", index, size));
                var actualIndex = InternalIndex(index);
                buffer[actualIndex] = value;
            }
        }

        #region IEnumerable<T> implementation

        public IEnumerator<T> GetEnumerator()
        {
            var segments = new ArraySegment<T>[2] { ArrayOne(), ArrayTwo() };
            foreach (var segment in segments)
                for (var i = 0; i < segment.Count; i++)
                    yield return segment.Array[segment.Offset + i];
        }

        #endregion

        #region IEnumerable implementation

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        #endregion

        public T Front()
        {
            ThrowIfEmpty();
            return buffer[start];
        }

        public T Back()
        {
            ThrowIfEmpty();
            return buffer[(end != 0 ? end : size) - 1];
        }

        public void PushBack(T item)
        {
            if (IsFull)
            {
                buffer[end] = item;
                Increment(ref end);
                start = end;
            }
            else
            {
                buffer[end] = item;
                Increment(ref end);
                ++size;
            }
        }

        public void PushFront(T item)
        {
            if (IsFull)
            {
                Decrement(ref start);
                end = start;
                buffer[start] = item;
            }
            else
            {
                Decrement(ref start);
                buffer[start] = item;
                ++size;
            }
        }

        public void PopBack()
        {
            ThrowIfEmpty("Cannot take elements from an empty buffer.");
            Decrement(ref end);
            buffer[end] = default(T);
            --size;
        }

        public void PopFront()
        {
            ThrowIfEmpty("Cannot take elements from an empty buffer.");
            buffer[start] = default(T);
            Increment(ref start);
            --size;
        }

        public T[] ToArray()
        {
            var newArray = new T[Size];
            var newArrayOffset = 0;
            var segments = new ArraySegment<T>[2] { ArrayOne(), ArrayTwo() };
            foreach (var segment in segments)
            {
                Array.Copy(segment.Array, segment.Offset, newArray, newArrayOffset, segment.Count);
                newArrayOffset += segment.Count;
            }

            return newArray;
        }

        private void ThrowIfEmpty(string message = "Cannot access an empty buffer.")
        {
            if (IsEmpty)
                throw new InvalidOperationException(message);
        }

        private void Increment(ref int index)
        {
            if (++index == Capacity)
                index = 0;
        }

        private void Decrement(ref int index)
        {
            if (index == 0)
                index = Capacity;
            index--;
        }

        private int InternalIndex(int index)
        {
            return start + (index < Capacity - start ? index : index - Capacity);
        }

        #region Array items easy access.

        private ArraySegment<T> ArrayOne()
        {
            if (start < end)
                return new ArraySegment<T>(buffer, start, end - start);
            return new ArraySegment<T>(buffer, start, buffer.Length - start);
        }

        private ArraySegment<T> ArrayTwo()
        {
            if (start < end)
                return new ArraySegment<T>(buffer, end, 0);
            return new ArraySegment<T>(buffer, 0, end);
        }

        #endregion
    }
}