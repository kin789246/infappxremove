using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using infappxremoval;

namespace infappxremove
{
    class Program
    {
        private static string infListFileName;
        private static readonly string logFileName = Path.GetFileNameWithoutExtension(System.Reflection.Assembly.GetExecutingAssembly().Location) + ".log";

        private static PowershellHelper psh;
        private static PnputilHelper puh;
        private static DevconHelper dh;
        private static List<PnputilData> installedInfList;
        private static List<PnputilData> baseInfList;
        private static List<PnputilData> extInfList;
        private static List<PnputilData> swcInfList;
        private static List<Win32PnpSignedDriverData> hwIdOemInfList;
        
        private static string checkLog = "Log in " + logFileName + ".";
        private static string logEnd = "\n\n######### Log End #########\n";
        private static string invalidPara = "Invalid parameter.";

        //get Intel HD audio extension inf
        private static string intelHdAudioExtInf = string.Empty;
        //get HD Audio Controller HardwareId
        private static string hdAudioControllerId = string.Empty;

        static int Main(string[] args)
        {
            string usage = "Usage: infappxremove [V | L | U]\n" 
                + "V = check version\n"
                + "L = list installed inf | load inf from inf list | Ex. infappxremove L list.txt\n"
                + "U = uninstall driver | Ex. infappxremove U list.txt";

            var time = DateTime.Now;
            string logStart = "######### Start log at " + time.ToLocalTime() + " #########\n\n";
            WriteLog(logStart);

            if (args.Length == 0)
            {
                Console.WriteLine(usage);
                WriteLog(invalidPara + logEnd);
                Console.WriteLine(invalidPara);
                return 1;
            }
            if (args[0] == "v" || args[0] == "V")
            {
                if (args.Length != 1)
                {
                    Console.WriteLine(usage);
                    WriteLog(invalidPara + logEnd);
                    Console.WriteLine(invalidPara);
                    return 1;
                }
                PrintVersion();
                WriteLog(logEnd);
                return 0;
            }
            else if (args[0] == "l" || args[0] == "L")
            {
                // list
                if (args.Length == 1)
                {
                    GettingInstalledList().Wait();
                    ClassifyInfList(installedInfList);
                    PrintClassifiedInfToLog();
                }
                else if (args.Length == 2)
                {
                    infListFileName = args[1];

                    if (CheckListFileName(infListFileName))
                    {
                        GettingInstalledList().Wait();
                        
                        List<PnputilData> result = GoSearchInfs(infListFileName);
                        ClassifyInfList(result);
                        PrintClassifiedInfToLog();
                    }
                    else
                    {
                        WriteLog(logEnd);
                        return 1;
                    }
                }
                else
                {
                    Console.WriteLine(usage);
                    WriteLog(invalidPara + logEnd);

                    return 1;
                }
            }
            else if (args[0] == "u" || args[0] == "U")
            {
                // uninstall
                if (args.Length != 2)
                {
                    Console.WriteLine(usage);
                    WriteLog(invalidPara + logEnd);
                    return 1;
                }

                infListFileName = args[1];

                if (CheckListFileName(infListFileName))
                {
                    ProcessUninstallation(infListFileName).Wait();
                }
                else
                {
                    WriteLog(logEnd);
                    return 1;
                }
            }
            else
            {
                Console.WriteLine(usage);
                WriteLog(invalidPara + logEnd);
                return 1;
            }
            
            WriteLog(logEnd);
            Console.WriteLine(checkLog);
            return 0;
        }

        private static void PrintClassifiedInfToLog()
        {
            WriteLog("######### Base Inf #########\n");
            PrintInfList(baseInfList);
            WriteLog("\n######### Extension Inf #########\n");
            PrintInfList(extInfList);
            WriteLog("\n######### Software Component Inf #########\n");
            PrintInfList(swcInfList);
        }

        private static void WriteLog(string text)
        {
            using (System.IO.StreamWriter file =
            new System.IO.StreamWriter(logFileName, true))
            {
                file.WriteLine(text);
            }
        }

        private static bool CheckListFileName(string listname)
        {
            try
            {
                Regex rgx = new Regex(@"\w*\.inf");
                string line;
                using (System.IO.StreamReader file = new System.IO.StreamReader(listname))
                {
                    while ((line = file.ReadLine()) != null)
                    {
                        if (!rgx.IsMatch(line))
                        {
                            Console.WriteLine("{0} is not a correct list file format.", listname);
                            WriteLog(listname + " is not a correct list file format.");
                            return false;
                        }
                    }
                }

            }
            catch (Exception exp)
            {
                Console.WriteLine(exp.Message);
                WriteLog(exp.Message);
                return false;
            }
            
            return true;
        }
        
        private static List<PnputilData> GoSearchInfs(string listname)
        {
            List<PnputilData> infToRemove = new List<PnputilData>();

            try
            {
                string line;
                using (System.IO.StreamReader file = new System.IO.StreamReader(listname))
                {
                    while ((line = file.ReadLine()) != null)
                    {
                        foreach (var item in installedInfList)
                        {
                            if (item.OriginalName.Equals(line.Trim(), StringComparison.OrdinalIgnoreCase))
                            {
                                infToRemove.Add(item);
                            }
                        }
                    }
                }
            }
            catch (Exception exp)
            {
                Console.WriteLine(exp.Message);
                WriteLog(exp.Message);
            }

            return infToRemove;
        }

        private static async Task ProcessUninstallation(string listname)
        {
            await GettingInstalledList();

            List<PnputilData> infToRemove = GoSearchInfs(listname);

            if (infToRemove.Count == 0)
            {
                Console.WriteLine("There is no inf files could be uninstalled.");
                WriteLog("There is no inf files could be uninstalled.");
                return;
            }

            ClassifyInfList(infToRemove);

            await UninstallFromInfList(swcInfList);
            await UninstallFromInfList(baseInfList);
            await UninstallFromInfList(extInfList);
        }

        private static async Task UninstallFromInfList(List<PnputilData> infList)
        {
            puh = new PnputilHelper();
            dh = new DevconHelper();

            string s = string.Empty;
            foreach (var item in infList)
            {
                string description = string.Empty;
                foreach (var des in item.Descriptions)
                {
                    if (des.ToLower().Contains("audio controller"))
                    {
                        description = des;
                    }
                }
                if (string.IsNullOrEmpty(description)) // if inf is not audio controller remove device than inf
                {
                    List<string> instanceIds = new List<string>();
                    foreach (var hwid in item.HardwareIds)
                    {
                        instanceIds.AddRange(await dh.FindAll(hwid));
                    }

                    foreach (var id in instanceIds)
                    {
                        WriteLog("Removing " + item.OriginalName + " " + id);
                        s = await dh.RemoveInstanceId(id);
                        WriteLog(s);
                    }

                    s = await puh.DeleteDriver(item.PublishedName);
                    WriteLog(s);

                    if (s.ToLower().Contains("failed") && item.PublishedName.Equals(intelHdAudioExtInf, StringComparison.OrdinalIgnoreCase))
                    {
                        WriteLog("Please uninstall High Definition Audio Controller in Device Manager, then uninstall again.");
                    }
                }
                else // if audio controll only remove inf
                {
                    s = await puh.DeleteAndUninstallDriver(item.PublishedName);
                    WriteLog(s);
                }
            }
        }

        private async static Task GettingInstalledList()
        {
            Console.Write("loading Win32PnpSignedDriver information...");

            psh = new PowershellHelper();
            hwIdOemInfList = await psh.GetWin32PnpSignedDriverData();

            Console.WriteLine("done.");

            puh = new PnputilHelper();
            Console.Write("Loading all of the installed inf information...");
            installedInfList = await puh.EnumDrivers();

            Console.WriteLine("done.");

            GetHwId(installedInfList);
            ProcessInfListOrder(installedInfList);
        }

        private static void PrintInfList(List<PnputilData> list)
        {
            if (list.Count == 0)
            {
                WriteLog("There is no inf installed on the system.");
                return;
            }

            int n = 1;
            foreach (var item in list)
            {
                WriteLog(item.ToLogString(n++));
            }
        }

        private static void PrintVersion()
        {
            var versionInfo = FileVersionInfo.GetVersionInfo(Assembly.GetEntryAssembly().Location);
           
            Console.WriteLine("{0} {1}", versionInfo.FileVersion, versionInfo.LegalCopyright);
            WriteLog(versionInfo.FileVersion + " " + versionInfo.LegalCopyright);
        }

        private static void GetHwId(List<PnputilData> list)
        {
            if (hwIdOemInfList.Count == 0)
            {
                return;
            }

            for (int i = 0; i < list.Count; i++)
            {
                foreach (var item in hwIdOemInfList)
                {
                    if (item.InfName.Equals(list[i].PublishedName, StringComparison.OrdinalIgnoreCase))
                    {
                        if (!string.IsNullOrEmpty(item.FriendlyName))
                        {
                            list[i].FriendlyNames.Add(item.FriendlyName);
                        }
                        if (!string.IsNullOrEmpty(item.HardwareId))
                        {
                            list[i].HardwareIds.Add(item.HardwareId);
                        }
                        if (!string.IsNullOrEmpty(item.Description))
                        {
                            list[i].Descriptions.Add(item.Description);
                        }
                    }
                }
            }
        }

        private static void ClassifyInfList(List<PnputilData> datas)
        {
            baseInfList = new List<PnputilData>();
            extInfList = new List<PnputilData>();
            swcInfList = new List<PnputilData>();

            if (datas.Count == 0)
            {
                return;
            }
            
            intelHdAudioExtInf = string.Empty;
            hdAudioControllerId = string.Empty;
            
            foreach (var pnpdata in datas)
            {
                switch (pnpdata.ClassName)
                {
                    case PnputilData.InfClass.Base:
                        baseInfList.Add(pnpdata);
                        break;
                    case PnputilData.InfClass.Extensions:
                        extInfList.Add(pnpdata);
                        break;
                    case PnputilData.InfClass.SoftwareComponets:
                        swcInfList.Add(pnpdata);
                        break;
                    default:
                        break;
                }

                //find Intel HD audio extension inf
                if (pnpdata.OriginalName.ToLower().Contains("IntcDAudioExt".ToLower())
                    || pnpdata.OriginalName.ToLower().Contains("HdBusExt".ToLower()))
                {
                    intelHdAudioExtInf = pnpdata.PublishedName;
                }

                //find ISST audio controller inf
                foreach (var des in pnpdata.Descriptions)
                {
                    if (des.ToLower().Contains("audio controller") && pnpdata.HardwareIds.Count > 0)
                    {
                        hdAudioControllerId = pnpdata.HardwareIds[0];
                        break;
                    }
                }
            }
        }

        private static void ProcessInfListOrder(List<PnputilData> list)
        {
            if (list.Count == 0)
            {
                return;
            }

            try
            {
                PnputilData[] temp = new PnputilData[4];

                int i = 0;
                while (i < list.Count)
                {
                    if (list[i].Descriptions.Count > 0)
                    {
                        //Intel Graphics
                        if (list[i].Descriptions[0].ToLower().Contains("graphics")
                            && list[i].HardwareIds[0].ToLower().Contains("pci\\ven_8086"))
                        {
                            temp[0] = list[i];
                            list.RemoveAt(i);
                            continue;
                        }
                        //ISST
                        if (list[i].Descriptions[0].ToLower().Contains("smart sound technology")
                            && list[i].HardwareIds[0].ToLower().Contains("intelaudio\\ctlr"))
                        {
                            temp[1] = list[i];
                            list.RemoveAt(i);
                            continue;
                        }
                        //ISST OED
                        if (list[i].Descriptions[0].ToLower().Contains("smart sound technology")
                            && list[i].HardwareIds[0].ToLower().Contains("intelaudio\\dsp_ctlr"))
                        {
                            temp[2] = list[i];
                            list.RemoveAt(i);
                            continue;
                        }
                        //ISST Audio Controller
                        if (list[i].Descriptions[0].ToLower().Contains("smart sound technology")
                            && list[i].HardwareIds[0].ToLower().Contains("pci\\ven_8086"))
                        {
                            temp[3] = list[i];
                            list.RemoveAt(i);
                            continue;
                        }
                    }
                    i++;
                }
                foreach (var item in temp)
                {
                    if (item != null)
                    {
                        list.Add(item);
                    }
                }
            }
            catch (Exception exp)
            {
                Console.WriteLine(exp.Message);
                WriteLog(exp.Message);
            }
        }
    }
}
