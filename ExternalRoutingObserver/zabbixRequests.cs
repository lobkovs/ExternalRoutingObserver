using System;
using System.Collections.Generic;
//using System.Threading;
using System.Diagnostics;
using System.Timers;
using NLog;

namespace ExternalRoutingObserver
{
    class zabbixRequests
    {
        // Очередь запросов в Zabbix
        private Queue<string> zabbixRequestsQueue;
        // Лог
        private Logger log = LogManager.GetCurrentClassLogger();
        // Таймер обработки очереди
        private Timer timer;

        string zabbixSenderPath = Properties.Settings.Default.zabbixSenderPath;

        // Время срабатывания очистки очереди
        int qTime = (int)TimeSpan.FromSeconds(Properties.Settings.Default.queueStartEverySeconds).TotalMilliseconds;

        public zabbixRequests()
        {
            log.Debug("Zabbix sender path: \"{0}\"", zabbixSenderPath);
            log.Debug("Start zabbix queue clean every seconds: \"{0}\" s, \"{1}\" ms", Properties.Settings.Default.queueStartEverySeconds, qTime);

            zabbixRequestsQueue = new Queue<string>();

            // Queue clean (gC) timer
            timer = new Timer(qTime);
            timer.AutoReset = true;
            timer.Elapsed += new ElapsedEventHandler(q_Tick);
        }

        public void q_Tick(object sender, ElapsedEventArgs e)
        {
            log.Trace("Выполнен запуск очистки очереди на отправку в Zabbix в \"{0}\"", DateTime.Now);
            log.Trace("В очереди \"{0}\" элементов.", zabbixRequestsQueue.Count);

            // Сепарированный вывод в лог элементов в очереди
            foreach (string elem in zabbixRequestsQueue.ToArray())
                log.Trace("Элемент очереди: \"{0}\"", elem);

            // Если очередь пуста, остановим таймер разбора очереди, для того чтобы не гонять впустую обработчик.
            if (zabbixRequestsQueue.Count == 0 && timer.Enabled == true)
            {
                timer.Stop();
                log.Trace("Таймер очереди остановлен!");
                return;
            }

            Console.WriteLine("Count queue: {0}", zabbixRequestsQueue.Count);

            string request = zabbixRequestsQueue.Dequeue();

            // Call zabbix agent
            Process.Start(zabbixSenderPath, request);

            log.Info("Аргументы для Zabbix отправлены: {0}", request);
        }

        /// <summary>
        /// Выполняет постановку в очередь на отправку в Zabbix
        /// </summary>
        /// <param name="req"></param>
        /// <returns></returns>
        public void Put(string req)
        {
            try
            {
                zabbixRequestsQueue.Enqueue(req);

                // Запустим таймер очереди, если в очереди есть элементы и таймер остановлен
                if (zabbixRequestsQueue.Count > 0 && timer.Enabled == false)
                {
                    timer.Start();
                    log.Debug("Таймер очереди запущен!");
                }

                log.Debug("Аргументы \"{0}\", поставлены в очередь на отправку в Zabbix", req);
            }
            catch (Exception ex)
            {
                log.Error("Не удалось добавить в очередь аргументы: \"{0}\". Описание ошибки: \"{1}\"", req, ex.Message);
            }
        }
    }
}
