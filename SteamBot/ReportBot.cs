﻿using StaffControlPanelServer.Classes;
using SteamKit2;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

public class ReportBot
{
    public static ReportBot _Instance;
    private static string _authCode;
    private static bool _isRunning;
    private static CallbackManager _manager;
    private static SteamClient _steamClient;
    private static SteamFriends _steamFriends;
    private static SteamUser _steamUser;
    private static string _twoFactorAuth;
    private static string _username, _password;
    private static List<ulong> adminIds;

    public ReportBot()
    {
#if DEBUG

        return;
#endif
        RP();
        _Instance = this;
    }

    public void Announce()
    {
        foreach (ulong i in adminIds) _steamFriends.SendChatMessage(i, EChatEntryType.Entered, "--Online Again!--");
    }

    public void RP()
    {
        adminIds = new List<ulong>
        {
            76561198023392824,
            //Crushed
            76561199062921803,
            //Death
        };
        _username = Environment.GetEnvironmentVariable("RUSTOPS_STEAM_USERNAME") ?? string.Empty;
        _password = Environment.GetEnvironmentVariable("RUSTOPS_STEAM_PASSWORD") ?? string.Empty;
        Steam_login();
    }

    public async Task SendReport(string rp)
    {
        foreach (ulong i in adminIds)
        {
            Console.WriteLine("Sendingmsg to " + i);
            _steamFriends.SendChatMessage(i, EChatEntryType.ChatMsg, rp);
            await Task.Delay(2000).ConfigureAwait(false);
        }
    }

    public async Task SendString(string rp)
    {
        foreach (ulong i in adminIds)
        {
            Console.WriteLine("Sendingmsg to " + i);
            _steamFriends.SendChatMessage(i, EChatEntryType.ChatMsg, rp);

            await Task.Delay(2500).ConfigureAwait(false);
        }
    }

    private void OnAccountInfo(SteamUser.AccountInfoCallback obj)
    {
        _steamFriends.SetPersonaState(EPersonaState.Online);
    }

    private void OnChatMessage(SteamFriends.FriendMsgCallback obj)
    {
        if (obj.EntryType == EChatEntryType.ChatMsg)
        {
            Console.WriteLine(obj.Message);
            if (obj.Message.Contains("rename"))
            {
                string name = obj.Message.Replace("rename ", "");

                _steamFriends.SetPersonaName(name);
            }
        }
        /*
        if (obj.EntryType == EChatEntryType.ChatMsg)
        {
            Console.Write("New message from ");
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine(_steamFriends.GetFriendPersonaName(obj.Sender));
            Console.ResetColor();
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "HourBoostr\\HourBoostr.exe");
            var titanPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Titan\\titan.exe");
            switch (obj.Message.Split(' ')[0])
            {
                case "!report":
                    try
                    {
                        var unused = obj.Message.Split(' ')[1];
                    }
                    catch
                    {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine("\t\tInvalid Syntax. Sending help");
                        Console.ResetColor();
                        _steamFriends.SendChatMessage(obj.Sender, EChatEntryType.ChatMsg,
                            "Please use !report [steamID]");
                        break;
                    }

                    var rparam = "report -t" + obj.Message.Split(' ')[1] + " -i 1 --secure";
                    RecognizeMessage(obj);
                    _steamFriends.SendChatMessage(obj.Sender, EChatEntryType.ChatMsg, "Reporting...");
                    try
                    {
                        Process.Start(titanPath, rparam);
                    }
                    catch
                    {
                        Console.ForegroundColor = ConsoleColor.Magenta;
                        Console.Write("Please put the Titan folder under ");
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine(AppDomain.CurrentDomain.BaseDirectory);
                        Console.ResetColor();
                        _steamFriends.SendChatMessage(obj.Sender, EChatEntryType.ChatMsg,
                            "Error reporting. Titan folder not found.");
                    }

                    Thread.Sleep(TimeSpan.FromSeconds(8));
                    _steamFriends.SendChatMessage(obj.Sender, EChatEntryType.ChatMsg,
                        "The Target was PROBABLY reported provided the bots were free.");
                    break;

                case "!commend":
                    try
                    {
                        var unused = obj.Message.Split(' ')[1];
                    }
                    catch
                    {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine("\t\tInvalid Syntax. Sending help");
                        _steamFriends.SendChatMessage(obj.Sender, EChatEntryType.ChatMsg,
                            "Please use !commend [steamID]");
                        Console.ResetColor();
                        break;
                    }

                    var cparam = "commend -t" + obj.Message.Split(' ')[1] + " -i 1 --secure";
                    RecognizeMessage(obj);
                    _steamFriends.SendChatMessage(obj.Sender, EChatEntryType.ChatMsg, "Commending...");
                    try
                    {
                        Process.Start(titanPath, cparam);
                    }
                    catch
                    {
                        Console.ForegroundColor = ConsoleColor.Magenta;
                        Console.Write("Please put the Titan folder under ");
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine(AppDomain.CurrentDomain.BaseDirectory);
                        Console.ResetColor();
                        _steamFriends.SendChatMessage(obj.Sender, EChatEntryType.ChatMsg,
                            "Error commending. Titan folder not found.");
                    }

                    Thread.Sleep(TimeSpan.FromSeconds(8));
                    _steamFriends.SendChatMessage(obj.Sender, EChatEntryType.ChatMsg,
                        "The Target was PROBABLY commended if the bots were free.");
                    break;

                case "!start":
                    RecognizeMessage(obj);
                    _steamFriends.SendChatMessage(obj.Sender, EChatEntryType.ChatMsg, "Starting HourBoostr");
                    try
                    {
                        Process.Start(path);
                    }
                    catch
                    {
                        Console.ForegroundColor = ConsoleColor.Magenta;
                        Console.Write("Please put the HourBoostr folder under ");
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine(AppDomain.CurrentDomain.BaseDirectory);
                        Console.ResetColor();
                        _steamFriends.SendChatMessage(obj.Sender, EChatEntryType.ChatMsg,
                            "Error Starting HourBoostr. HourBoostr folder not found.");
                    }

                    break;

                case "!status":
                    RecognizeMessage(obj);
                    var pname = Process.GetProcessesByName("HourBoostr");
                    _steamFriends.SendChatMessage(obj.Sender, EChatEntryType.ChatMsg,
                        pname.Length == 0 ? "HourBoostr is not running." : "HourBoostr is running.");
                    break;

                case "!help":
                    RecognizeMessage(obj);
                    _steamFriends.SendChatMessage(obj.Sender, EChatEntryType.ChatMsg,
                        "Use !start to start HourBoostr");
                    _steamFriends.SendChatMessage(obj.Sender, EChatEntryType.ChatMsg,
                        "Use !stop to stop HourBoostr");
                    _steamFriends.SendChatMessage(obj.Sender, EChatEntryType.ChatMsg,
                        "Use !status to check running status");
                    _steamFriends.SendChatMessage(obj.Sender, EChatEntryType.ChatMsg,
                        "Use !report http://steamcommunity.com/id/zzzzzzzzzz to report a mo-fo");
                    _steamFriends.SendChatMessage(obj.Sender, EChatEntryType.ChatMsg,
                        "Use !commend http://steamcommunity.com/id/zzzzzzzzzzzz to commend");
                    break;

                case "!stop":
                    RecognizeMessage(obj);
                    _steamFriends.SendChatMessage(obj.Sender, EChatEntryType.ChatMsg,
                        "Stopping HourBoostr if running.");
                    Process.Start("cmd.exe", "/c taskkill /IM HourBoostr.exe");
                    break;

                default:
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.Write("\t\tCommand not recognized ");
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine(obj.Message);
                    Console.ResetColor();
                    _steamFriends.SendChatMessage(obj.Sender, EChatEntryType.ChatMsg,
                        "Sorry command not recognized. Send !help for help.");
                    break;
            }
        }
        */
    }

    private void OnConnected(SteamClient.ConnectedCallback obj)
    {
        Console.WriteLine("Connected to Steam! Logging in '{0}'...", _username);
        byte[] sentryHash = null;
        if (File.Exists("sentry.bin"))
        {
            // if we have a saved sentry file, read and sha-1 hash it
            byte[] sentryFile = File.ReadAllBytes("sentry.bin");
            sentryHash = CryptoHelper.SHAHash(sentryFile);
        }
        //Console.WriteLine(sentryHash);

        _steamUser.LogOn(new SteamUser.LogOnDetails
        {
            Username = _username,
            Password = _password,

            // in this sample, we pass in an additional auth-code
            // this value will be null (which is the default) for our first logon attempt
            AuthCode = _authCode,

            // if the account is using 2-factor auth, we'll provide the two factor code instead
            // this will also be null on our first logon attempt
            TwoFactorCode = _twoFactorAuth,

            // our subsequent log-ons use the hash of the sentry file as proof of ownership of the file
            // this will also be null for our first (no auth-code) and second (auth-code only) logon attempts
            SentryFileHash = sentryHash
        });
    }

    private void OnDisconnected(SteamClient.DisconnectedCallback obj)
    {
        Console.WriteLine("Disconnected from steam. Reconnecting... ");

        //isRunning = false; //we don't need it

        //We gotta wait here to steam to completely disconnect
        //System.Threading.Thread.Sleep(TimeSpan.FromSeconds(5));

        //RECONNECT TO STEAM now with the correct 2fa
        _steamClient.Connect();
    }

    private void OnFriendList(SteamFriends.FriendsListCallback obj)
    {
        Thread.Sleep(2500);
        foreach (SteamFriends.FriendsListCallback.Friend friend in obj.FriendList)
            if (friend.Relationship == EFriendRelationship.RequestRecipient)
            {
                Console.Write("\t\tFriend added ");
                Console.ForegroundColor = ConsoleColor.Blue;
                Console.WriteLine(_steamFriends.GetFriendPersonaName(friend.SteamID));
                Console.ResetColor();
                _steamFriends.AddFriend(friend.SteamID);
                Thread.Sleep(1000);
                _steamFriends.SendChatMessage(friend.SteamID, EChatEntryType.ChatMsg,
                    "Hi, I'll send you reports from your server.");
            }
    }

    private void OnLoggedOff(SteamUser.LoggedOffCallback obj)
    {
        Console.WriteLine("Logged out of steam {0}", obj.Result);
    }

    private void OnLoggedOn(SteamUser.LoggedOnCallback obj)
    {
        bool isSteamGuard = obj.Result == EResult.AccountLogonDenied;
        bool is2Fa = obj.Result == EResult.AccountLoginDeniedNeedTwoFactor;
        if (isSteamGuard || is2Fa)
        {
            Console.WriteLine("This account is SteamGuard protected!");
            if (is2Fa)
            {
                Console.Write("Please enter your 2 factor auth code from your authenticator app: ");
                _twoFactorAuth = Console.ReadLine();
            }
            else
            {
                Console.Write("Please enter the auth code sent to the email at {0}: ", obj.EmailDomain);
                _authCode = Console.ReadLine();
            }

            return;
        }

        if (obj.Result != EResult.OK)
        {
            Console.WriteLine("Unable to logon to Steam: {0} / {1}", obj.Result, obj.ExtendedResult);
            _isRunning = false;
            return;
        }

        Console.WriteLine("Successfully logged into steam with id {0}", _username);

        Announce();

        //SteamUser.LogOff();
    }

    private void OnMachineAuth(SteamUser.UpdateMachineAuthCallback callback)
    {
        Console.WriteLine("Updating sentry file...");

        // write out our sentry file
        // ideally we'd want to write to the filename specified in the callback
        // but then this sample would require more code to find the correct sentry file to read during logon
        // for the sake of simplicity, we'll just use "sentry.bin"
        int fileSize;
        byte[] sentryHash;
        using (FileStream fs = File.Open("sentry.bin", FileMode.OpenOrCreate, FileAccess.ReadWrite))
        {
            fs.Seek(callback.Offset, SeekOrigin.Begin);
            fs.Write(callback.Data, 0, callback.BytesToWrite);
            fileSize = (int)fs.Length;
            fs.Seek(0, SeekOrigin.Begin);
            using (SHA1 sha = SHA1.Create())
            {
                sentryHash = sha.ComputeHash(fs);
            }
        }

        // inform the steam servers that we're accepting this sentry file
        _steamUser.SendMachineAuthResponse(new SteamUser.MachineAuthDetails
        {
            JobID = callback.JobID,
            FileName = callback.FileName,
            BytesWritten = callback.BytesToWrite,
            FileSize = fileSize,
            Offset = callback.Offset,
            Result = EResult.OK,
            LastError = 0,
            OneTimePassword = callback.OneTimePassword,
            SentryFileHash = sentryHash
        });
        Console.WriteLine("Done!");
    }

    private void RecognizeMessage(SteamFriends.FriendMsgCallback obj)
    {
        Console.Write("\t\tCommand recognized ");
        Console.ForegroundColor = ConsoleColor.Blue;
        Console.WriteLine(obj.Message);
        Console.ResetColor();
    }

    private void Steam_login()
    {
        try
        {
            _steamClient = new SteamClient();
            //DotaGCHandler.Bootstrap(_steamClient);
            //_dota = _steamClient.GetHandler<DotaGCHandler>();
            _manager = new CallbackManager(_steamClient);
            _steamUser = _steamClient.GetHandler<SteamUser>();
            _steamFriends = _steamClient.GetHandler<SteamFriends>();
            _manager.Subscribe<SteamClient.ConnectedCallback>(OnConnected);
            _manager.Subscribe<SteamClient.DisconnectedCallback>(OnDisconnected);
            _manager.Subscribe<SteamUser.LoggedOnCallback>(OnLoggedOn);
            _manager.Subscribe<SteamUser.LoggedOffCallback>(OnLoggedOff);
            _manager.Subscribe<SteamUser.AccountInfoCallback>(OnAccountInfo);
            _manager.Subscribe<SteamUser.UpdateMachineAuthCallback>(OnMachineAuth);
            _manager.Subscribe<SteamFriends.FriendMsgCallback>(OnChatMessage);
            _manager.Subscribe<SteamFriends.FriendsListCallback>(OnFriendList);
            Console.WriteLine("Connecting to steam in 3s");
            _steamClient.Connect();

            Console.WriteLine("AdminId Count : " + adminIds.Count());

            _isRunning = true;
            //while (_isRunning)
            // in order for the callbacks to get routed, they need to be handled by the manager
            _ = new OneShotTimer(_manager.RunWaitCallbacks, 1);
        }
        catch (Exception e)
        {
            Sentry.SentrySdk.CaptureException(e);
#if DEBUG
            throw;
#endif
        }
    }
}
