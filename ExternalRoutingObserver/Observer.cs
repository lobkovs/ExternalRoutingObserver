using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Timers;
using NLog;

namespace ExternalRoutingObserver
{
    class Observer
    {
        private Logger log = LogManager.GetCurrentClassLogger();

        bool checkRealTimeProcessed = Properties.Settings.Default.enableCheckRealTimeProcessed;

        // Queue on send into Zabbix
        zabbixRequests zabbixQueue;

        DateTime dNew;
        DateTime dOld = DateTime.Now; // Default is program start work time

        int timeBetweenChangeEventRaising = Properties.Settings.Default.timeBetweenChangeEventRaising;
        int tresholdProcessingLog = Properties.Settings.Default.tresholdProcessingLogMinutes;

        // Garbage Collector (gC) time
        int gCTime = (int)TimeSpan.FromSeconds(Properties.Settings.Default.startGarbageCollectEverySeconds).TotalMilliseconds;
        // Garbage Collector (gC) Timer
        private Timer gC;

        string watchDir = Properties.Settings.Default.watchDir;
        string watchFilter = Properties.Settings.Default.watchFilter;
        string zabbixLogTextMetricName = Properties.Settings.Default.zabbixLogTextMetricNameSuffix;
        string zabbixRealTimeMetricName = Properties.Settings.Default.zabbixRealTimeMetricNameSuffix;
        string zabbixServer = Properties.Settings.Default.zabbixServer;
        string zabbixNetworkNode = Properties.Settings.Default.zabbixNetworkNode;

        // List of the log files aka LocalDB
        List<LogFile> logs = new List<LogFile>();
        RegexOptions rExpOptions = RegexOptions.IgnoreCase;
        FileSystemWatcher watcher = new FileSystemWatcher();

        public Observer()
        {
            Console.WriteLine("Start application watcher");
            Console.WriteLine("Watch on dir: \"{0}\" on {1}", watchDir, dOld);

            log.Debug("START PROGRAM!!!");
            log.Debug("Watch on dir: \"{0}\" on \"{1}\"", watchDir, dOld);
            log.Debug("Watch filter: \"{0}\"", watchFilter);
            log.Debug("Enable check realtime processed: \"{0}\"", checkRealTimeProcessed);
            log.Debug("Start garbage collect every seconds: \"{0}\" s, \"{1}\" ms", Properties.Settings.Default.startGarbageCollectEverySeconds, gCTime);
            log.Debug("Treshold processing log: \"{0}\" minutes", tresholdProcessingLog);
            log.Debug("Zabbix send enable: \"{0}\"", Properties.Settings.Default.zabbixEnableSend);
            log.Debug("Zabbix from log text metric name suffix: \"{0}\"", zabbixLogTextMetricName);
            log.Debug("Zabbix realtime metric name suffix: \"{0}\"", zabbixRealTimeMetricName);
            log.Debug("Zabbix server: \"{0}\"", zabbixServer);
            log.Debug("Zabbix network node: \"{0}\"", zabbixNetworkNode);
            log.Debug("Zabbix count metrics: \"{0}\"", Properties.Settings.Default.zabbixCountMetric);

            zabbixQueue = new zabbixRequests();

            watcher.Path = watchDir;
            watcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size;
            watcher.Filter = watchFilter;

            // Register event handlers
            watcher.Created += new FileSystemEventHandler(OnCreated);
            watcher.Changed += new FileSystemEventHandler(OnChanged);

            // GarbageCollector (gC) timer
            gC = new Timer(gCTime);
            gC.AutoReset = true;
            gC.Elapsed += new ElapsedEventHandler(GC_Tick);

            log.Debug("Timer was initialization");

            //new AutoResetEvent(false).WaitOne();
        }

        public void Start()
        {
            watcher.EnableRaisingEvents = true;
            log.Info("ExternalRoutingObserver is started.");
        }

        public void Stop()
        {
            watcher.EnableRaisingEvents = false;
            log.Info("ExternalRoutingObserver is stopped.");
        }

        /// <summary>
        /// GarbageCollector handler
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void GC_Tick(object sender, ElapsedEventArgs e)
        {
            log.Trace("GarbageCollector is start in \"{0}\"", DateTime.Now);

            // Stop gC timer if list of logs is empty
            if (logs.Count == 0 && gC.Enabled == true)
            {
                gC.Stop();
                log.Trace("Logs count is \"{0}\". GarbageCollector is stoped!", logs.Count);
                return;
            }

            // Print into app log, elem from list
            foreach (LogFile elem in logs)
                log.Trace("Elem in list: \"{0}\"", elem.fileName);

            log.Debug("The gC processed in {0}.", DateTime.Now);

            // Looping list of item
            foreach (LogFile logElem in logs.Reverse<LogFile>())
            {
                GlobalDiagnosticsContext.Set("rid", logElem.fileName);
                // Clear closed the log
                if (logElem.fileCloseFromRealTime != default(DateTime))
                {
                    log.Trace("Processed logs garbadge collect!");
                    try
                    {
                        // Delete elem from local DB
                        logs.Remove(logElem);

                        // Write info
                        string output = string.Format("Лог \"{0}\" был удалён в {1}. Дата закрытия лога (realTime) {2}, (fromLogText) {3}.", logElem.fileName, DateTime.Now, logElem.fileCloseFromRealTime, logElem.fileCloseFromLogText);
                        Console.WriteLine(output);
                        log.Info(output);

                        #region ???
                        // For debug real time
                        //if (checkRealTimeProcessed)
                        //{
                        //    output = string.Format("Файл \"{0}\" был закрыт спустя (realTime) {1}, (fromLogText) {2}.", logElem.fileName, getSubstractSecondsFromNowInt(logElem.fileStartFromRealTime), logElem.getSecondsRealTimeFromCloseToStart());
                        //    Console.WriteLine(output);
                        //    log.Debug(output);
                        //    // Send to zabbix
                        //    sendToZabbixNumeric(logElem.altName, getSubstractSecondsFromNowInt(logElem.fileStartFromRealTime));
                        //}
                        #endregion
                    }
                    catch (Exception ex)
                    {
                        string errorMess = String.Format("Не удалось очистить сборщиком мусора файл \"{0}\".", logElem.fileName);
                        log.Error(ex, errorMess);
                    }
                }

                // Check and send to zabbix file processing real time
                if (checkRealTimeProcessed && logElem.fileCloseFromRealTime == default(DateTime))
                {
                    log.Trace("Check time to open file!");
                    int subtractTime = logElem.getSecondsRealTimeFromCloseToStart();

                    log.Debug("Время обработки файла \"{0}\" в реальном времи (сек): \"{1}\"", logElem.fileName, subtractTime);
                    // Send to zabbix
                    sendToZabbixNumeric(logElem.altNameRealTime, subtractTime);
                }

                // Drop log element if execution time more than "treshold processing log" in settings
                if (logElem.getMinutesRealTimeFromCloseToStart() > tresholdProcessingLog)
                {
                    log.Trace("Log element execution is too long, more that \"{0}\" minutes! Look at settings file! Drop him!!!", tresholdProcessingLog);
                    // Delete elem from local DB
                    logs.Remove(logElem);

                    // Get real time minutes execution form start processing this log
                    int time = logElem.getMinutesRealTimeFromCloseToStart();

                    // Write warning
                    string output = string.Format("Лог \"{0}\" был сброшен. Время выполнения лога \"{1}\" минуты. Максимальное время выполнения определённое настройками \"{2}\" минуты!", logElem.fileName, time, tresholdProcessingLog);
                    Console.WriteLine(output);
                    log.Warn(output);

                    // Prepare data for zabbix
                    output = string.Format("WARNING, {0} was dropped! Current log execution time {1} minutes. Max execution time {2} minutes.", logElem.fileName, time, tresholdProcessingLog);

                    // Send to zabbix
                    sendToZabbixString(output);
                }

                GlobalDiagnosticsContext.Set("rid", "");
            }
        }

        private void OnChanged(object sender, FileSystemEventArgs e)
        {
            GlobalDiagnosticsContext.Set("rid", e.Name);

            if (!string.IsNullOrEmpty(Properties.Settings.Default.regexpAllowHandleFileName))
            {
                if (Regex.IsMatch(e.Name, Properties.Settings.Default.regexpAllowHandleFileName, rExpOptions) == false)
                {
                    log.Warn("Изменение произошло в логе \"{0}\", к обработке принимаются файлы вида \"{1}\"", e.Name, Properties.Settings.Default.regexpAllowHandleFileName);
                    return;
                }
            }
            else
                log.Info("Начало обработки лога \"{0}\"", e.Name);

            // Update new date
            dNew = DateTime.Now;
            // Calculation how much time passed between event calls
            int callTimesAgo = Convert.ToInt32(dNew.Subtract(dOld).TotalMilliseconds);

            log.Debug("Сработало событие \"Change\" на файле \"{0}\" в {1:yyyy-MM-dd H:mm:ss}, миллисекунд с предыдущего срабатываения {2}", e.Name, dNew, callTimesAgo);

            // This "if", because for example SublimeText generate save event twice
            if (callTimesAgo < timeBetweenChangeEventRaising)
                return;

            LogFile logModel = getLogModelFromLocalDB(e.Name);

            // If is not error and log file is not processed
            if (logModel.hasError == false && logModel.fileCloseFromRealTime == default(DateTime))
            {
                try
                {
                    using (FileStream fstream = new FileStream(e.FullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    {
                        fstream.Position = logModel.offsetLine;

                        StreamReader sr = new StreamReader(fstream);

                        TimeSpan detectMetricTime;
                        string line;

                        while ((line = sr.ReadLine()) != null)
                        {
                            // Increment number of the line
                            logModel.currentLine++;

                            // Try parse start date time from the log and set into logModel
                            if (logModel.fileStartFromRealTime == default(DateTime))
                            {
                                logModel.fileStartFromLogText = getTimeFromLogText(line);
                                logModel.fileStartFromRealTime = DateTime.Now;

                                string output = string.Format("Начало обработки файла \"{0}\", в (realTime) {1}, (fromLogText) {2}", logModel.fileName, logModel.fileStartFromRealTime, logModel.fileStartFromLogText);
                                Console.WriteLine(output);
                                log.Info(output);

                                log.Debug("Отправим данные в заббикс для маркирования начала обработки файла (оссобенность отображения графиков в заббиксе)!");
                                // Send to zabbix value 0 for start process marking
                                // Realtime
                                sendToZabbixNumeric(logModel.altNameRealTime, 0);
                                // Logtext
                                sendToZabbixNumeric(logModel.altNameLogText, 0);
                            }

                            #region CHECK Section

                            // /////////////////
                            // Try detect routeID
                            // /////////////////
                            if (Regex.IsMatch(line, Properties.Settings.Default.regexpDetectRoutindId) && string.IsNullOrEmpty(logModel.routeId))
                            {
                                log.Debug("Найден routeId! Шаблон: \"{0}\". В (realTime) {1}, (fromLogText) {2}", Properties.Settings.Default.regexpDetectRoutindId, DateTime.Now, getTimeFromLogText(line));
                                string suffix = @"trid=";
                                string pattern = @"\d+";
                                string tempString = Regex.Match(line, suffix + pattern).Value;

                                logModel.routeId = Regex.Match(tempString, pattern).Value;

                                string output = string.Format("Для лога \"{0}\" установлен routingID = {1} в {2}", logModel.fileName, logModel.routeId, DateTime.Now);
                                Console.WriteLine(output);
                                log.Info(output);
                            }

                            // /////////////////
                            // Check on error
                            // /////////////////
                            if (Regex.IsMatch(line, Properties.Settings.Default.regexpError, rExpOptions))
                            {
                                logModel.hasError = true;

                                logModel.fileCloseFromLogText = getTimeFromLogText(line);
                                logModel.fileCloseFromRealTime = DateTime.Now;

                                log.Debug("Найдена ошибка в файле \"{0}\"! Шаблон: \"{1}\". В (realTime) {2}, (fromLogText) {3}", logModel.fileName, Properties.Settings.Default.regexpError, logModel.fileCloseFromRealTime, getTimeFromLogText(line));

                                // Calc time
                                int processedSecond = logModel.getSecondsLogTextFromCloseToStart();

                                // Write to user text
                                string output = String.Format("В логе \"{0}\" обнаружена ошибка в {1} на линии {2}, спустя {3}. Дальнейшая обработка остановлена!", logModel.fileName, logModel.fileCloseFromRealTime, logModel.currentLine, processedSecond);
                                Console.WriteLine(output);
                                log.Error(output);

                                // Send to zabbix for realtime
                                sendToZabbixNumeric(logModel.altNameRealTime, logModel.getSecondsRealTimeFromCloseToStart());
                                sendToZabbixNumeric(logModel.altNameRealTime, 0);

                                // Send to zabbix for log text
                                sendToZabbixNumeric(logModel.altNameLogText, processedSecond); // Send current time offset
                                sendToZabbixNumeric(logModel.altNameLogText, 0); // Finish(close) this processing for zabbix, send value 0
                                sendToZabbixStringCustom(logModel);
                                break;
                            }

                            // /////////////////
                            // Check on metrics
                            // /////////////////
                            // Get metrics
                            string[] metrics = Properties.Settings.Default.regexpMetrics
                                    .Split(';')
                                    .Where(z => !String.IsNullOrWhiteSpace(z))
                                    .ToArray();

                            // Hadle metrics
                            foreach (string metric in metrics)
                            {
                                if (Regex.IsMatch(line, metric.Trim(), rExpOptions))
                                {
                                    log.Debug("Найдена метрика \"{0}\"! В (realTime) {1}, (fromLogText) {2}", metric, DateTime.Now, getTimeFromLogText(line));
                                    detectMetricTime = getTimeFromLogText(line).Subtract(logModel.fileStartFromLogText);

                                    // Write to user text
                                    string outString = String.Format("Метрика \"{0}\", замечена спустя {1} на линии {2}", metric, detectMetricTime.ToString(), logModel.currentLine);
                                    Console.WriteLine(outString);
                                    log.Info(outString);

                                    // Send to zabbix
                                    sendToZabbixNumeric(logModel.altNameLogText, Convert.ToInt32(detectMetricTime.TotalSeconds));

                                    continue;
                                }
                            }

                            // /////////////////
                            // Check on finish
                            // /////////////////
                            if (Regex.IsMatch(line, Properties.Settings.Default.regexpEnd, rExpOptions))
                            {
                                log.Debug("Найдена метрика \"конца\" файла \"{0}\"! В (realTime) {1}, (fromLogText) {2}", Properties.Settings.Default.regexpEnd, DateTime.Now, getTimeFromLogText(line));
                                logModel.fileCloseFromLogText = getTimeFromLogText(line);
                                logModel.fileCloseFromRealTime = DateTime.Now;

                                int processedTime = logModel.getSecondsLogTextFromCloseToStart();

                                // Write to user text
                                string output = String.Format("Лог \"{0}\", был успешно обработан в {1} спустя {2}", logModel.fileName, logModel.fileCloseFromRealTime, processedTime);
                                Console.WriteLine(output);
                                log.Info(output);

                                // Send to zabbix for realtime
                                sendToZabbixNumeric(logModel.altNameRealTime, logModel.getSecondsRealTimeFromCloseToStart());
                                sendToZabbixNumeric(logModel.altNameRealTime, 0);

                                // Send to zabbix log text time
                                sendToZabbixNumeric(logModel.altNameLogText, processedTime); // Send current time offset
                                sendToZabbixNumeric(logModel.altNameLogText, 0); // Finish(close) this processing for zabbix, send value 0
                                sendToZabbixStringCustom(logModel);
                                break;
                            }
                            #endregion
                        }

                        logModel.offsetLine = fstream.Position;
                    }
                }
                catch (Exception ex)
                {
                    log.Error(ex.Message);
                    Console.WriteLine(ex.Message);
                }
            }
            else
            {
                string output = String.Format("Лог \"{0}\" уже содержит ошибку или был закрыт ранее.", logModel.fileName);
                Console.WriteLine(output);
                log.Warn(output);
            }

            // Update old date
            dOld = dNew;

            GlobalDiagnosticsContext.Set("rid", "");
        }

        protected void OnCreated(object sender, FileSystemEventArgs e)
        {
            string output = String.Format("The file \"{0}\" on path \"{1}\", was been {2}. Событие передано в обработчик \"OnChanged\"!", e.Name, e.FullPath, e.ChangeType);
            Console.WriteLine(output);
            log.Info(output);
            OnChanged(sender, e);
        }

        #region Helper

        /// <summary>
        /// Get object of the LogModel from local DB
        /// </summary>
        /// <param name="fileName"></param>
        /// <returns>item of the LogModel</returns>
        private LogFile getLogModelFromLocalDB(string fileName)
        {
            LogFile logModel;
            // Get object of the log model
            if (logs.Exists(x => x.fileName == fileName))
                logModel = logs.Where(x => x.fileName == fileName).First();
            else
            {
                // Initialize object of the log model
                logModel = new LogFile()
                {
                    fileName = fileName,
                    offsetLine = 0,
                    hasError = false,
                    routeId = "",
                    currentLine = 0
                };
                // Put into list logs
                logs.Add(logModel);

                // Start gC timer if list of logs is not empty
                if (logs.Count > 0 && gC.Enabled == false)
                {
                    gC.Start();
                    log.Trace("Logs count is not empty! GarbageCollector is started!");
                }

                // Set "alt name" for zabbix metrics name
                logModel.altNameLogText = getAltName(zabbixLogTextMetricName);
                logModel.altNameRealTime = getAltName(zabbixRealTimeMetricName);
            }
            return logModel;
        }

        /// <summary>
        /// Calculate alternative unique name for element
        /// </summary>
        /// <param name="suffix"></param>
        /// <returns></returns>
        private string getAltName(string suffix)
        {
            for (int n = 0; n <= Properties.Settings.Default.zabbixCountMetric; n++)
            {
                string tempMetricName = suffix + n.ToString();
                // Discover list of log
                if (!logs.Exists(x => (x.altNameLogText == tempMetricName) || (x.altNameRealTime == tempMetricName)))
                    return tempMetricName;
            }

            log.Warn("Не могу найти свободного имя метрики для суффикса: {0}. Поэтому возвращаю пустое имя и эта метрика не будет отображаться в заббиксе.", suffix);
            log.Trace("Количество элементов в массиве на текущий момент: {0}", logs.Count);
            log.Trace("Список элементов в массиве: {0}", string.Join("; ", logs.Select(x => x.fileName))); // Build list of logs filename into string for a write to log

            return "";
        }

        private DateTime getTimeFromLogText(string line)
        {
            string regexpPatterTime = @"^\d{2}:\d{2}:\d{2}";
            DateTime detectTime;

            if (DateTime.TryParseExact(Regex.Match(line, regexpPatterTime).Value, "HH:mm:ss", null, System.Globalization.DateTimeStyles.None, out detectTime))
            {
                log.Trace("Время получено при парсинге лог файла.");
                return detectTime;
            }
            else
            {
                log.Trace("Не удалось получить время из лог файла. Установим текущее время.");
                return DateTime.Now;
            }
        }

        private string getSubstractSecondsFromNowStr(DateTime dt)
        {
            return getSubstractSecondsFromNowInt(dt).ToString();
        }

        private int getSubstractSecondsFromNowInt(DateTime dt)
        {
            return Convert.ToInt32(DateTime.Now.Subtract(dt).TotalSeconds);
        }

        private void sendToZabbixNumeric(string keyData, int value)
        {
            // Init vars
            string zabbixExecArgs = String.Format("-z {0} -s {1} -k {2} -o {3}", zabbixServer, zabbixNetworkNode, keyData, value);
            // Send to zabbix
            sendToZabbix(zabbixExecArgs);
        }

        private void sendToZabbixStringCustom(LogFile logModel)
        {
            // Init vars
            int time = Convert.ToInt32(logModel.fileCloseFromLogText.Subtract(logModel.fileStartFromLogText).TotalSeconds);
            string value = String.Format("{0}, {1} routindId {2} processed in {3} sec. on line {4}. ({5}, {6})", logModel.hasError == true ? "ERROR" : "OK", logModel.fileName, logModel.routeId, time, logModel.currentLine, logModel.altNameLogText, logModel.altNameRealTime);

            sendToZabbixString(value);
        }

        private void sendToZabbixString(string text)
        {
            string zabbixExecArgs = String.Format("-z {0} -s {1} -k logproc.ExtOpt_Stats -o \"{2}\"", zabbixServer, zabbixNetworkNode, text);
            // Send to zabbix
            sendToZabbix(zabbixExecArgs);
        }

        private void sendToZabbix(string sendArgs)
        {
            if (Properties.Settings.Default.zabbixEnableSend == false)
                return;

            zabbixQueue.Put(sendArgs);
        }

        #endregion
    }
}