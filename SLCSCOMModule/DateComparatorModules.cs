using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EnterpriseManagement.Mom.Modules.DataItems;
using Microsoft.EnterpriseManagement.HealthService;
using Microsoft.EnterpriseManagement.HealthService.Modules.ModuleDebug;
using System.Xml.Serialization;
using System.Xml;
using Microsoft.EnterpriseManagement.Mom.Internal.Xml;
using System.IO;
using System.Security.Cryptography;
using Microsoft.EnterpriseManagement.Mom.Modules.DataItems.Event;
using System.Runtime.Remoting.Messaging;
using System.Security.AccessControl;
using System.Threading;
using System.Diagnostics;
using System.Reflection;


namespace SLCSCOMModules
{
    [MonitoringModule(ModuleType.ReadAction)]
    [ModuleOutput(true)]
    public class DateComparatorModulev1 : ModuleDebugBase<PropertyBagDataItem>
    {
        private readonly object shutdownLock = new object();
        private bool shutdown;
        private DataItemAcknowledgementCallback acknowledgeCallback;
        private string units;
        private string dvalue;
        private DateTimeOffset dtinput;
        private enum dtOpCode
        {
            Equal,
            NotEqual,
            Greater,
            Less,
            GreaterEqual,
            LessEqual
        }
        private dtOpCode opvalue;
        private int tvalue;
        private bool debug;
        private readonly string strFile = string.Concat(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"\Temp\", "DateComparator.log");
        protected string ModuleName => "DateComparator";
        public DateComparatorModulev1(ModuleHost<PropertyBagDataItem> moduleHost, XmlReader configuration, byte[] previousState)
        : base(moduleHost, ref configuration, previousState)
        {
            if (moduleHost == null)
            {
                throw new ArgumentNullException("moduleHost");
            }
            if (previousState != null)
            {
                throw new ArgumentOutOfRangeException("previousState");
            }
            if (configuration == null)
            {
                throw new ArgumentOutOfRangeException("configuration");
            }
            shutdown = false;

            //************
            try
            {
                //LogEvent("loading module configuration");
                XmlReaderHelper xmlReaderHelper = new XmlReaderHelper(configuration);
                configuration.MoveToContent();
                configuration.ReadStartElement("Configuration");

                units = configuration.ReadElementString("Units");
                dvalue = configuration.ReadElementString("DateTimeExpression");
                opvalue = (dtOpCode)Enum.Parse(typeof(dtOpCode),configuration.ReadElementString("Operator"));
                tvalue = xmlReaderHelper.ReadElementAsInt32("Value");
                debug = bool.Parse(xmlReaderHelper.ReadOptionalElementAsString("Debug", "false"));

                configuration.ReadEndElement();
                configuration.Close();
                LogEvent($"Loaded module configuration. Config values: {units}, {dvalue}, {opvalue}, {tvalue}");
                    
            }
            catch (XmlException ex2)
            {
                throw new ModuleException(ModuleName, ex2);
            }
            
            //********

        }
        [InputStream(0)]
        public void OnNewDataItems(DataItemBase dataItem, DataItemAcknowledgementCallback acknowledgeCallback, object acknowledgedState, DataItemProcessingCompleteCallback completionCallback, object completionState)
        {
            LogEvent("Entered OnNewDataItems");
            if ((acknowledgeCallback == null && completionCallback != null) || (acknowledgeCallback != null && completionCallback == null))
            {
                throw new ArgumentOutOfRangeException("acknowledgedCallback, completionCallback", "The acknowledge and completion callbacks must either both be null or valid callbacks, one was set the other was null");
            }
            lock (shutdownLock)
            {
                if (shutdown)
                {
                    return;
                }

                if (!shutdown)
                {
                    this.acknowledgeCallback = acknowledgeCallback;
                    if (debug)
                    {
                        XmlReader xml = dataItem.GetItemXml();
                        xml.MoveToContent();
                        LogEvent($"Received data item\n{xml.ReadOuterXml()}");
                    }
                    switch (dvalue)
                        {
                            case "FileTime":
                                {
                                dtinput = DateTimeOffset.FromFileTime(long.Parse(dvalue));
                                break;
                                }
                            case "UnixTimeSeconds":
                                {
                                dtinput = DateTimeOffset.FromUnixTimeSeconds(long.Parse(dvalue));
                                break;
                                }
                            case "UnixTimeMilliSeconds":
                                {
                                dtinput = DateTimeOffset.FromUnixTimeMilliseconds(long.Parse(dvalue));
                                break;
                                }
                            default:
                                {
                                dtinput = DateTimeOffset.Parse(dvalue);
                                break;
                                }
                        }

                }
                PropertyBagDataItem dataItem2 = new PropertyBagDataItem(null, 
                    new Dictionary<string, Dictionary<string, object>> { 
                                 { "", new Dictionary<string, object>() {
                                           { "Status", DateCompare(dtinput, DateTimeOffset.UtcNow, Tuple.Create(units,tvalue), out double diff) },
                                           { "Difference", diff }
                                 }
                                 }   
                    });
                LogEvent($"Posting output data item");
                
                base.ModuleHost.PostOutputDataItem(dataItem2, DataItemAcknowledged, acknowledgedState);
                completionCallback?.Invoke(completionState);
                
            }
        }

        public override void DoStart()
        {
            lock (shutdownLock)
            {

                if (shutdown)
                {
                    LogEvent("Shutdown in progress. Returning");
                    return;

                }
                else
                {
                    LogEvent("Requesting next data item.");
                    base.ModuleHost.RequestNextDataItem();
                }
            }
        }

        public override void DoShutdown()
        {
            LogEvent("Shutting down");
            lock (shutdownLock)
            {
                if (shutdown)
                {
                    return;
                }
                shutdown = true;
            }
        }

        

        private void DataItemAcknowledged(object acknowledgedState)
        {
            lock (shutdownLock)
            {
                if (!shutdown)
                {
                    acknowledgeCallback?.Invoke(acknowledgedState);
                    
                    base.ModuleHost.RequestNextDataItem();
                }
            }
        }

        private void LogEvent(string message)
        {
            if (debug)
            {
                File.AppendAllText(strFile, $"{DateTimeOffset.UtcNow.ToString("o")}\t{message}\n");
            }
        }

        private bool DateCompare(DateTimeOffset dt1, DateTimeOffset dt2)
        {
            switch (opvalue) {
                case dtOpCode.Equal: { return dt1 == dt2; }
                case dtOpCode.NotEqual: { return dt1 != dt2; }
                case dtOpCode.Greater: { return dt1 > dt2; }
                case dtOpCode.Less: { return dt1 < dt2; }
                case dtOpCode.GreaterEqual: { return dt1 >= dt2;  }
                case dtOpCode.LessEqual: { return dt1 <= dt2;  }
                default: { throw new ArgumentOutOfRangeException("Comparison operator"); }
            }
            
        }
        private bool DateCompare(DateTimeOffset dt1, DateTimeOffset dt2, Tuple<string, int> offset, out double diff)
        {
            TimeSpan diff2 = dt2 - dt1;
            switch (offset.Item1)
            {
                case "Seconds":
                    {
                        diff = Math.Round(diff2.TotalSeconds,1);
                        return DateCompare(dt1.AddSeconds(0 - offset.Item2), dt2);
                    }
                case "Minutes":
                    {
                        diff = Math.Round(diff2.TotalMinutes,1);
                        return DateCompare(dt1.AddMinutes(0 - offset.Item2), dt2);
                    }
                case "Hours":
                    {
                        diff = Math.Round(diff2.TotalHours,1);
                        return DateCompare(dt1.AddHours(0 - offset.Item2),dt2);
                    }
                case "Days":
                    {
                        diff = Math.Round(diff2.TotalDays,1);
                        return DateCompare(dt1.AddDays(0 - offset.Item2),dt2);
                    }
                case "Months":
                    {
                        diff = Math.Round(diff2.TotalDays / 30.4,1);
                        return DateCompare(dt1.AddMonths(0 - offset.Item2),dt2);
                    }
                case "Years":
                    {
                        diff = Math.Round(diff2.TotalDays/365.2425,1);
                        return DateCompare(dt1.AddYears(0 - offset.Item2),dt2);
                    }
                default: { throw new ArgumentOutOfRangeException("Units"); }
            }

        }
        
    }
    /// <summary>
    /// Supports batching
    /// </summary>
    [MonitoringModule(ModuleType.ReadAction)]
    [ModuleOutput(true)]
    public class DateComparatorModulev2 : ModuleDebugBase<PropertyBagDataItem>
    {
        private TextWriter tw;                                                                    
        
        private readonly object shutdownLock = new object();
        
        private bool shutdown;
        
        private DataItemAcknowledgementCallback acknowledgeCallback;
        
        private string units;
        
        private string dvalue;
        
        private DateTimeOffset dtinput;
        private enum dtOpCode
        {
            Equal,
            NotEqual,
            Greater,
            Less,
            GreaterEqual,
            LessEqual
        }
        
        private dtOpCode opvalue;
        
        private int tvalue;
        
        private bool debug;

        private readonly string strFile = string.Concat(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"\Temp\", "DateComparatorv2.log");
        
        protected string ModuleName => "DateComparator";
        public DateComparatorModulev2(ModuleHost<PropertyBagDataItem> moduleHost, XmlReader configuration, byte[] previousState)
        : base(moduleHost, ref configuration, previousState)
        {
            var fs = new FileStream(strFile, FileMode.Append, FileSystemRights.AppendData, FileShare.ReadWrite, 4096, FileOptions.None);
            var sw = new StreamWriter(fs);
            tw = StreamWriter.Synchronized(sw);

            if (moduleHost == null)
            {
                throw new ArgumentNullException("moduleHost");
            }
            if (previousState != null)
            {
                throw new ArgumentOutOfRangeException("previousState");
            }
            if (configuration == null)
            {
                throw new ArgumentOutOfRangeException("configuration");
            }
            shutdown = false;

                
            try
            {
            LogEvent($"Loading module configuration. Module version {Assembly.GetExecutingAssembly().ImageRuntimeVersion}");
            XmlReaderHelper xmlReaderHelper = new XmlReaderHelper(configuration);
            configuration.MoveToContent();
            configuration.ReadStartElement("Configuration");

            units = configuration.ReadElementString("Units");
            dvalue = configuration.ReadElementString("DateTimeExpression");
            opvalue = (dtOpCode)Enum.Parse(typeof(dtOpCode), configuration.ReadElementString("Operator"));
            tvalue = xmlReaderHelper.ReadElementAsInt32("Value");
            debug = bool.Parse(xmlReaderHelper.ReadOptionalElementAsString("Debug", "false"));

            configuration.ReadEndElement();
            configuration.Close();
            LogEvent($"Loaded module configuration. Config values: {units}, {dvalue}, {opvalue}, {tvalue}");

            }
            catch (XmlException ex2)
            {
                base.ModuleHost.NotifyError(ModuleErrorSeverity.FatalError, new ModuleException(ModuleName, ex2));
                //throw new ModuleException(ModuleName, ex2);
            }
            
                //********

        }
        [InputStream(0)]
        public void OnNewDataItems(DataItemBase[] dataItems, bool isBatchLogicalSet, DataItemAcknowledgementCallback acknowledgeCallback, object acknowledgedState, DataItemProcessingCompleteCallback completionCallback, object completionState)
        {
            if ((acknowledgeCallback == null && completionCallback != null) || (acknowledgeCallback != null && completionCallback == null))
            {
                base.ModuleHost.NotifyError(ModuleErrorSeverity.FatalError, new ArgumentOutOfRangeException("acknowledgedCallback, completionCallback", "The acknowledge and completion callbacks must either both be null or valid callbacks, one was set the other was null"));
                return;
            }
            lock (shutdownLock)
            {
                if (shutdown)
                {
                    return;
                }
                PropertyBagDataItem[] outputDataItems = null;
                if (dataItems != null)
                {
                    outputDataItems = new PropertyBagDataItem[dataItems.Length];
                }

                if (!shutdown)
                {
                    this.acknowledgeCallback = acknowledgeCallback;

                    for (int i = 0; i < dataItems.Length; i++)
                    {
                        DataItemBase dataItem = dataItems[i];
                        if (debug)
                        {
                           XmlReader xml = dataItem.GetItemXml();
                           xml.MoveToContent();
                           LogEvent($"Received data item\n{xml.ReadOuterXml()}");
                        }
                        
                        switch (dvalue)
                        {
                            case "FileTime":
                                {
                                    dtinput = DateTimeOffset.FromFileTime(long.Parse(dvalue));
                                    break;
                                }
                            case "UnixTimeSeconds":
                                {
                                    dtinput = DateTimeOffset.FromUnixTimeSeconds(long.Parse(dvalue));
                                    break;
                                }
                            case "UnixTimeMilliSeconds":
                                {
                                    dtinput = DateTimeOffset.FromUnixTimeMilliseconds(long.Parse(dvalue));
                                    break;
                                }
                            default:
                                {
                                    dtinput = DateTimeOffset.Parse(dvalue);
                                    break;
                                }
                        }
                        PropertyBagDataItem dataItem2 = new PropertyBagDataItem(null,
                            new Dictionary<string, Dictionary<string, object>> {
                                 { "", new Dictionary<string, object>() {
                                           { "Status", DateCompare(dtinput, DateTimeOffset.UtcNow, Tuple.Create(units,tvalue), out double diff) },
                                           { "Difference", diff }
                                 }
                                 }
                            });
                        outputDataItems[i] = dataItem2;
                    }
                }
                if (outputDataItems != null)
                {
                    //LogEvent($"Posting output data item");
                    base.ModuleHost.PostOutputDataItems(outputDataItems, isBatchLogicalSet, DataItemAcknowledged, acknowledgedState);
                }
                completionCallback?.Invoke(completionState);

            }
        }

        public override void DoStart()
        {
            
            lock (shutdownLock)
            {

                if (shutdown)
                {
                    LogEvent("Shutdown in progress. Returning");
                    return;

                }
                else
                {
                    //LogEvent("Requesting next data item.");
                    base.ModuleHost.RequestNextDataItem();
                }
            }
        }

        public override void DoShutdown()
        {
            //LogEvent("Shutting down");
            lock (shutdownLock)
            {
                if (shutdown)
                {
                    return;
                }
                tw.Close();
                shutdown = true;
            }
        }



        private void DataItemAcknowledged(object acknowledgedState)
        {
            lock (shutdownLock)
            {
                if (!shutdown)
                {
                    //LogEvent("Calling AcknowledgeCallback");
                    acknowledgeCallback?.Invoke(acknowledgedState);

                    base.ModuleHost.RequestNextDataItem();
                }
            }
        }

        private void LogEvent(string message)
        {
            if (debug)
            {
                string logEntry = $"{DateTimeOffset.UtcNow.ToString("o")}\t{message}\n";
                tw.WriteLine(logEntry);
                tw.Flush();

            }
        }

        private bool DateCompare(DateTimeOffset dt1, DateTimeOffset dt2)
        {
            switch (opvalue)
            {
                case dtOpCode.Equal: { return dt1 == dt2; }
                case dtOpCode.NotEqual: { return dt1 != dt2; }
                case dtOpCode.Greater: { return dt1 > dt2; }
                case dtOpCode.Less: { return dt1 < dt2; }
                case dtOpCode.GreaterEqual: { return dt1 >= dt2; }
                case dtOpCode.LessEqual: { return dt1 <= dt2; }
                default: { throw new ArgumentOutOfRangeException("Comparison operator"); }
            }

        }
        private bool DateCompare(DateTimeOffset dt1, DateTimeOffset dt2, Tuple<string, int> offset, out double diff)
        {
            TimeSpan diff2 = dt1 - dt2;
            switch (offset.Item1)
            {
                case "Seconds":
                    {
                        diff = Math.Round(diff2.TotalSeconds, 1);
                        return DateCompare(dt1.AddSeconds(0 - offset.Item2), dt2);
                    }
                case "Minutes":
                    {
                        diff = Math.Round(diff2.TotalMinutes, 1);
                        return DateCompare(dt1.AddMinutes(0 - offset.Item2), dt2);
                    }
                case "Hours":
                    {
                        diff = Math.Round(diff2.TotalHours, 1);
                        return DateCompare(dt1.AddHours(0 - offset.Item2), dt2);
                    }
                case "Days":
                    {
                        diff = Math.Round(diff2.TotalDays, 1);
                        return DateCompare(dt1.AddDays(0 - offset.Item2), dt2);
                    }
                case "Months":
                    {
                        diff = Math.Round(diff2.TotalDays / 30.4, 1);
                        return DateCompare(dt1.AddMonths(0 - offset.Item2), dt2);
                    }
                case "Years":
                    {
                        diff = Math.Round(diff2.TotalDays / 365.2425, 1);
                        return DateCompare(dt1.AddYears(0 - offset.Item2), dt2);
                    }
                default: { throw new ArgumentOutOfRangeException("Units"); }
            }

        }

    }
}
