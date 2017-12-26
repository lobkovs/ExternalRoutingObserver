using System.IO;
using NLog;
using Topshelf;

namespace ExternalRoutingObserver
{
    class Program
    {
        static void Main(string[] args)
        {
            Logger log = LogManager.GetCurrentClassLogger();
            log.Debug("#################################");
            log.Debug("Начало выполнение программы!");
            log.Debug("Запускаем программу как службу Windows!");
            HostFactory.Run(x =>
            {
                x.StartAutomatically();
                x.Service<Observer>(s =>
                {
                    s.ConstructUsing(hostSettings => new Observer());
                    s.WhenStarted(tc => tc.Start());
                    s.WhenStopped(tc => tc.Stop());
                });
                x.RunAsLocalSystem();

                x.SetDescription(string.Format(@"Наблюдает за процессом выполнения внешней маршрутизации и отправляет метрики в Zabbix! Служба смонтирована в папке {0}.", Directory.GetCurrentDirectory()));
                x.SetDisplayName("ExternalRoutingObserver");
                x.SetServiceName("ExternalRoutingObserver");
            });
        }
    }
}