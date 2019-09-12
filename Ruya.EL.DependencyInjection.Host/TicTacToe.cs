namespace Ruya.EL.DependencyInjection.Host
{
    public class TicTacToe : IGame
    {
        private string _status;

        public TicTacToe()
        {
            Name = "Tic Tac Toe";
            CurrentPlayers = 0;
            MinPlayers = 2;
            MaxPlayers = 2;
            _status = "No active games";
        }

        #region IGame Members

        public string Name { get; set; }
        public int CurrentPlayers { get; set; }
        public int MinPlayers { get; set; }
        public int MaxPlayers { get; set; }

        public void AddPlayer()
        {
            CurrentPlayers++;
        }

        public void RemovePlayer()
        {
            CurrentPlayers--;
        }

        public void Play()
        {
            if ((CurrentPlayers > MaxPlayers) ||
                (CurrentPlayers < MinPlayers))
            {
                _status = $"{Name}: It's not possible to play with {CurrentPlayers} players.";
            }
            else
            {
                _status = $"{Name}: Playing with {CurrentPlayers} players.";
            }
        }

        public string Result()
        {
            return _status;
        }

        #endregion
    }
}