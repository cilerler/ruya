namespace Ruya.MAF.Host.AddIns.Calculator
{
    internal class Command
    {
        internal Command(string line)
        {
            string[] parts = line.Trim().Split(' ');
            A = double.Parse(parts[0]);
            Action = parts[1];
            B = double.Parse(parts[2]);
        }

        public double A { get; }
        public double B { get; }
        public string Action { get; }
    }
}
