namespace Concealment
{
    public class SecondaryLogging : IDisposable
    {
        public enum EntryType { PLUGIN, KEEPALIVE, CONCEAL_BASIC, CONCEAL_DYNAMIC, REVEAL_BASIC, REVEAL_DYNAMIC, SHUTDOWN, EXCLUDE_CHECK }

        const string LOG_FILE = "ConcealmentSecondary.log";
        const int WRITE_DELAY_MILLIS = 3000;
        
        readonly string _absolutePath;

        Timer _writeTimer = null;
        bool _timerRunning = false;
        Task _writeTask = null;
        Queue<string> _queue = new Queue<string>();
        StringBuilder _batchBuilder = new StringBuilder();

        public static readonly SecondaryLogging Instance;

        
        
        static SecondaryLogging()
        {
            Instance = new SecondaryLogging();
        }

        // Instance constructor.
        SecondaryLogging(string logDirectoryAbsolute)
        {
            _absolutePath = Path.Combine(logDirectoryAbsolute, LOG_FILE);
        }
        // Instance destructor. Fallback disposal of timer.
        ~SecondaryLogging()
        {
            FinishWorkAndDestroy();
        }
        public void Dispose()
        {
            FinishWorkAndDestroy();
            GC.SuppressFinalize(this);
        }
        void FinishWorkAndDestroy()
        {
            _writeTimer?.Dispose();
            _writeTask?.Wait();
            bool write;
            lock (_queue)
            {
                write = _queue.Count > 0;
            }
            if (write) WriteLogEntriesAsync().Wait();
        }

        public void Log(EntryType type, string customMessage, DateTime dtNow = DateTime.Now, bool writeImmediately)
        {
            lock (_queue)
            {
                _queue.Enqueue($"[{dtNow:yyyy-MM-dd || HH:mm:ss.fff}] {type}: {customMessage}");
            }
            if (writeImmediately) StartEntryWriter();
            else InitTimer();
        }


        void InitTimer()
        {
            if (_timerRunning) _writeTimer.Change(WRITE_DELAY_MILLIS, Timeout.Infinite);
            else _writeTimer = new Timer(StartEntryWriter, null, WRITE_DELAY_MILLIS, Timeout.Infinite);
            _timerRunning = true;
        }

        public void StartEntryWriter()
        {
            _writeTimer?.Dispose();
            _timerRunning = false;
            _writeTask?.Wait();
            _writeTask = WriteLogEntriesAsync();
        }

        public async Task WriteLogEntriesAsync()
        {
            List<string> lines;
            lock (_queue)
            {
                if (_queue.Count == 0) return;
                lines = _queue.ToList();
                _queue.Clear();
            }
            lock (_batchBuilder)
            {
                foreach (var line in lines) _batchBuilder.AppendLine(line);
                using (FileStream stream = new FileStream(_absolutePath, FileMode.Append, FileAccess.Write))
                {
                    using (StreamWriter writer = new StreamWriter(stream))
                    {
                        writer.WriteLine(_batchBuilder.ToString().TrimEnd('\n'));
                    }
                }
                _batchBuilder.Clear();
            }
        }
    }
}