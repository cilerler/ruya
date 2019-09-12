namespace Ruya.EL.DependencyInjection.Host
{
    public class Table
    {
        private readonly IGame _game;

        public Table(IGame game)
        {
            _game = game;
        }

        public string GameStatus()
        {
            return _game.Result();
        }

        public void AddPlayer()
        {
            _game.AddPlayer();
        }

        public void RemovePlayer()
        {
            _game.RemovePlayer();
        }

        public void Play()
        {
            _game.Play();
        }
    }
}