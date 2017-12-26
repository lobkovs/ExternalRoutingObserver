using System;

namespace ExternalRoutingObserver
{
    // Models of cocaine :)
    public class LogFile
    {
        public string altNameLogText { get; set; }
        public string altNameRealTime { get; set; }
        public string fileName { get; set; }
        public string routeId { get; set; }
        public DateTime fileStartFromLogText { get; set; }
        public DateTime fileStartFromRealTime { get; set; }
        public DateTime fileCloseFromLogText { get; set; }
        public DateTime fileCloseFromRealTime { get; set; }
        public long offsetLine { get; set; }
        public bool hasError { get; set; }
        public int currentLine { get; set; }

        public int getSecondsRealTimeFromCloseToStart()
        {
            if (fileCloseFromRealTime != default(DateTime) && fileStartFromRealTime != default(DateTime))
                return Convert.ToInt32(fileCloseFromRealTime.Subtract(fileStartFromRealTime).TotalSeconds);
            else if (fileStartFromRealTime != default(DateTime) && fileCloseFromRealTime == default(DateTime))
                return Convert.ToInt32(DateTime.Now.Subtract(fileStartFromRealTime).TotalSeconds);
            else
                return 0;
        }

        public int getMinutesRealTimeFromCloseToStart()
        {
            return getSecondsRealTimeFromCloseToStart() / 60;
        }

        public int getSecondsLogTextFromCloseToStart()
        {
            if (fileCloseFromLogText != default(DateTime) && fileStartFromLogText != default(DateTime))
                return Convert.ToInt32(fileCloseFromLogText.Subtract(fileStartFromLogText).TotalSeconds);
            else if (fileStartFromLogText != default(DateTime) && fileCloseFromLogText == default(DateTime))
                return Convert.ToInt32(DateTime.Now.Subtract(fileStartFromLogText).TotalSeconds);
            else
                return 0;
        }
    }
}
