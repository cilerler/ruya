namespace Ruya.Composition.Interfaces
{
    public interface IExtension
    {
        string Name { set; get; }
        string Command(string input);
    }
}
