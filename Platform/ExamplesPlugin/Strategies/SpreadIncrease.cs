using System;

namespace TickZoom.Examples
{
    public class SpreadIncrease
    {
        private double startingSpread;
        private double increaseSpread;
        private double currentSpread;
        private double nextIncrease;
        public SpreadIncrease(double startingSpread, double increaseSpread)
        {
            this.startingSpread = startingSpread;
            this.increaseSpread = increaseSpread;
            currentSpread = startingSpread;
        }

        public double CurrentSpread
        {
            get { return currentSpread; }
            set { currentSpread = value; }
        }

        public double StartingSpread
        {
            get { return startingSpread; }
        }

        public double IncreaseSpread
        {
            get { return increaseSpread; }
        }

        public void Increase()
        {
            currentSpread += nextIncrease;
            nextIncrease += IncreaseSpread;
        }

        public void Decrease()
        {
            nextIncrease -= IncreaseSpread;
            nextIncrease = Math.Max(nextIncrease, 0);
        }

        public void Reset()
        {
            currentSpread = StartingSpread;
            nextIncrease = 0D;
        }
    }
}