﻿using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using UnityEngine;
using static StaffControlPanelServer.SteamBot.Arkan;

namespace StaffControlPanelServer.SteamBot
{
    public class Arkan
    {
        public class AdminConfig
        {
            public ViolationsLog violationsLog = new ViolationsLog();
        }

        public class AIMViolationData
        {
            public string ammoShortName;
            public List<string> attachments = new List<string>();
            public string attackerMountParentName;
            public string bodyPart;
            public float calculatedTravelDistance;
            public float damage;
            public float distanceDifferenceViolation = 0f;
            public float drag;
            public float firedProjectileFiredTime;
            public Vector3 firedProjectileInitialPosition;
            public Vector3 firedProjectileInitialVelocity;
            public Vector3 firedProjectilePosition;
            public float firedProjectileTravelTime;
            public Vector3 firedProjectileVelocity;
            public DateTime firedTime;
            public float forgivenessModifier = 1f;
            public float gravityModifier;
            public bool hasFiredProjectile = false;
            public string hitInfoBoneName;
            public string hitInfoHitEntityPlayerName;
            public string hitInfoHitEntityPlayerUserID;
            public Vector3 hitInfoHitPositionWorld;
            public string hitInfoInitiatorPlayerName;
            public string hitInfoInitiatorPlayerUserID;
            public Vector3 hitInfoPointEnd;
            public Vector3 hitInfoPointStart;
            public float hitInfoProjectileDistance;
            public float hitInfoProjectilePrefabDrag;
            public float hitInfoProjectilePrefabGravityModifier;
            public List<HitData> hitsData = new List<HitData>();
            public bool isAttackerMount = false;
            public bool isEqualFiredProjectileData = true;
            public bool isPlayerPositionToProjectileStartPositionDistanceViolation = false;
            public bool isTargetMount = false;
            public float physicsSteps = 32f;
            public Vector3 playerEyesLookAt;
            public Vector3 playerEyesPosition;
            public int projectileID;
            public Vector3 startProjectilePosition;
            public Vector3 startProjectileVelocity;
            public string targetMountParentName;
            public int violationID;
            public string weaponShortName;
        }

        public class EmbedFieldList
        {
            public bool inline { get; set; }
            public string name { get; set; }
            public string value { get; set; }
        }

        public class FiredProjectile
        {
            public string ammoShortName;
            public List<string> attachments = new List<string>();
            public DateTime firedTime;
            public List<ProjectileRicochet> hitsData = new List<ProjectileRicochet>();
            public bool isChecked;
            public bool isMounted;
            public string mountParentName;
            public Vector3 mountParentPosition;
            public Vector4 mountParentRotation;
            public float NRProbabilityModifier = 1f;
            public Vector3 playerEyesLookAt;
            public Vector3 playerEyesPosition;
            public Vector3 projectilePosition;
            public Vector3 projectileVelocity;
            public string weaponShortName;
            public uint weaponUID;
        }

        public class FiredShotsData
        {
            public string ammoShortName;
            public List<string> attachments = new List<string>();
            public SortedDictionary<int, FiredProjectile> firedShots = new SortedDictionary<int, FiredProjectile>();
            public string weaponShortName;
        }

        public class HitData
        {
            public float delta = 1f;
            public float distanceFromHitPointToProjectilePlane = 0f;
            public ProjectileRicochet hitData;
            public Vector3 hitPointEnd;
            public Vector3 hitPointStart;
            public Vector3 hitPositionWorld;
            public bool isHitPointNearProjectilePlane = true;
            public bool isHitPointNearProjectileTrajectoryLastSegmentEndPoint = true;
            public bool isHitPointOnProjectileTrajectory = true;
            public bool isLastSegmentOnProjectileTrajectoryPlane = true;
            public bool isProjectileStartPointAtEndReverseProjectileTrajectory = true;
            public Vector3 lastSegmentPointEnd;
            public Vector3 lastSegmentPointStart;
            public Vector3 pointProjectedOnLastSegmentLine;
            public Vector3 reverseLastSegmentPointEnd;
            public Vector3 reverseLastSegmentPointStart;
            public int side;
            public Vector3 startProjectilePosition;
            public Vector3 startProjectileVelocity;
            public float travelDistance = 0f;
        }

        public class InRockViolationData
        {
            public DateTime dateTime;
            public float drag;
            public FiredProjectile firedProjectile;
            public float gravityModifier;
            public float physicsSteps;
            public int projectileID;
            public Vector3 rockHitPosition;
            public string targetBodyPart;
            public float targetDamage;
            public float targetHitDistance;
            public Vector3 targetHitPosition;
            public string targetID;
            public string targetName;
        }

        public class InRockViolationsData
        {
            public DateTime dateTime;

            public Dictionary<int, InRockViolationData> inRockViolationsData =
                new Dictionary<int, InRockViolationData>();
        }

        public class NoRecoilViolationData
        {
            public string ammoShortName;
            public List<string> attachments = new List<string>();
            public bool isMounted;
            public Vector3 mountParentPosition;
            public Vector4 mountParentRotation;
            public int NRViolationsCnt;
            public int ShotsCnt;

            public Dictionary<int, SuspiciousProjectileData> suspiciousNoRecoilShots =
                new Dictionary<int, SuspiciousProjectileData>();

            public float violationProbability;
            public string weaponShortName;
        }

        public class PlayerFiredProjectlesData
        {
            public SortedDictionary<int, FiredProjectile> firedProjectiles =
                new SortedDictionary<int, FiredProjectile>();

            public bool isChecked;
            public float lastFiredTime;
            public SortedDictionary<uint, MeleeThrown> melees = new SortedDictionary<uint, MeleeThrown>();
            public float physicsSteps = 32f;
            public ulong PlayerID;
            public string PlayerName;
        }

        public class PlayersViolationsData
        {
            public DateTime lastChangeTime;
            public DateTime lastSaveTime;
            public int mapSize;
            public Dictionary<ulong, PlayerViolationsData> Players = new Dictionary<ulong, PlayerViolationsData>();
            public int seed;
            public string serverTimeStamp;
        }

        public class PlayerViolationsData
        {
            public SortedDictionary<string, AIMViolationData> AIMViolations =
                new SortedDictionary<string, AIMViolationData>();

            public SortedDictionary<string, InRockViolationsData> inRockViolations =
                new SortedDictionary<string, InRockViolationsData>();

            public SortedDictionary<string, NoRecoilViolationData> noRecoilViolations =
                new SortedDictionary<string, NoRecoilViolationData>();

            public ulong PlayerID;
            public string PlayerName;
        }

        public class TrajectorySegment
        {
            public Vector3 pointEnd;
            public Vector3 pointStart;
        }

        public class ViolationsLog
        {
            public int AIMViolation;
            public int InRockViolation;
            public int NoRecoilViolation;
            public ulong steamID;
        }

        public class WeaponConfig
        {
            public bool AIMDetectEnabled;
            public bool NRDetectEnabled;
            public int NRMinShotsCountToCheck;
            public float NRViolationProbability;
            public float weaponMaxTimeShotsInterval;
            public float weaponMinTimeShotsInterval;
        }

        public struct MeleeThrown
        {
            public float drag;
            public DateTime firedTime;
            public float gravityModifier;
            public bool isMounted;
            public string meleeShortName;
            public uint meleeUID;
            public string mountParentName;
            public Vector3 mountParentPosition;
            public Vector4 mountParentRotation;
            public Vector3 playerEyesLookAt;
            public Vector3 playerEyesPosition;
            public Vector3 position;
            public float projectileVelocity;
        }

        public struct ProjectileRicochet
        {
            public Vector3 hitPosition;
            public Vector3 inVelocity;
            public Vector3 outVelocity;
            public int projectileID;
        }

        public struct SuspiciousProjectileData
        {
            public Vector3 closestPointLine1;
            public Vector3 closestPointLine2;
            public bool isNoRecoil;
            public bool isShootedInMotion;
            public Vector3 prevIntersectionPoint;
            public int projectile1ID;
            public Vector3 projectile1Position;
            public Vector3 projectile1Velocity;
            public int projectile2ID;
            public Vector3 projectile2Position;
            public Vector3 projectile2Velocity;
            public float recoilAngle;
            public float recoilScreenDistance;
            public float timeInterval;
            public DateTime timeStamp;
        }
    }

    public class Ticket
    {
        public ArkanType AType { get; set; }

        //public ArkanReport _report { get; set; }
        [JsonProperty("DateTime", NullValueHandling = NullValueHandling.Ignore)]
        public string DateTime { get; set; }

        [JsonProperty("ID", NullValueHandling = NullValueHandling.Ignore)]
        public long? ID { get; set; }

        [JsonProperty("Message", NullValueHandling = NullValueHandling.Ignore)]
        public string Message { get; set; }

        [JsonProperty("ReporterID", NullValueHandling = NullValueHandling.Ignore)]
        public string ReporterID { get; set; }

        [JsonProperty("ReporterName", NullValueHandling = NullValueHandling.Ignore)]
        public string ReporterName { get; set; }

        [JsonProperty("ReporterPos", NullValueHandling = NullValueHandling.Ignore)]
        public string ReporterPos { get; set; }

        public Server server { get; set; }

        [JsonProperty("SortableDateTime")] public object SortableDateTime { get; set; }

        [JsonProperty("TargetID")] public object TargetID { get; set; }

        [JsonProperty("TargetName")] public object TargetName { get; set; }

        [JsonProperty("TargetPos")] public object TargetPos { get; set; }

        [JsonProperty("TicketProcessed", NullValueHandling = NullValueHandling.Ignore)]
        public bool? TicketProcessed { get; set; }

        [JsonProperty("Title", NullValueHandling = NullValueHandling.Ignore)]
        public string Title { get; set; }

        public TicketType Type { get; set; }

        public enum ArkanType
        {
            None = 0,
            Aim = 1,
            Recoil = 2,
            Rock = 3
        }

        public enum Server
        {
            Modded = 0,
            Vanilla = 1,
            OneGrid = 2
        }

        public enum TicketType
        {
            none = 0,
            ViolationKick = 1,
            PlayerTicket = 2,
            Arkan = 3,
            PlayerReport = 4,
            Other = 5
        }

        public class ArkanReport
        {
            public AIMViolationData _aimData { get; set; }
            public NoRecoilViolationData _norecoildata { get; set; }
            public InRockViolationsData _rockdata { get; set; }
        }

        public string GetBeautyfiedMsg()
        {
            //《》「」『』【】〔〕⌐ ☰ ☲ 」「 📑  ⚠️  ⛔  ✅  🚫 ┓ ┏ ┗ ┛ ║ ▹ ◃ └ ┐ ┘ ┌ ☢ 💌💕💖💞💠💥💧💫💬💮💯💲💳💴💵💸💾📁📂📃📄📅📆📇📈📉📊📋📌📍
            if (Title.Contains("Violation"))
                Type = TicketType.ViolationKick;
            else if (Title.Contains("Ticket"))
                Type = TicketType.PlayerTicket;
            else if (Title.Contains("Report"))
                Type = TicketType.PlayerReport;
            else if (Title.Contains("Arkan"))
                Type = TicketType.Arkan;
            else if (Title.Contains("Other"))
                Type = TicketType.Other;
            string msgtosend = "";

            msgtosend += $"「{server.ToString()}」【{Title}】\n";

            switch (Type)
            {
                case TicketType.ViolationKick:

                    msgtosend += " \u26a0\ufe0f \n";
                    msgtosend += $"▹Kicked Player: {ReporterName} ({ReporterID}) \n";
                    msgtosend += $"At Position: {ReporterPos} \n \n";
                    msgtosend += $"▹Message: {Message} \n \n";

                    break;

                case TicketType.PlayerTicket:
                    msgtosend += " \ud83d\udcc3 \n";
                    msgtosend += $"▹Ticket From: {ReporterName} ({ReporterID}) \n";
                    msgtosend += $"At Position: {ReporterPos} \n \n";
                    msgtosend += $"▹Message: {Message} \n \n";

                    break;

                case TicketType.PlayerReport:

                    msgtosend += " \ud83d\udcd1 \n";
                    msgtosend += $"▹Report From: {ReporterName} ({ReporterID}) \n";
                    msgtosend += $"At Position: {ReporterPos} \n \n";
                    msgtosend += $"▹TargetPlayer: {TargetName} ({TargetID}) \n";
                    msgtosend += $"At Position: {TargetPos} \n \n";
                    msgtosend += $"▹Message: {Message} \n \n";

                    break;

                case TicketType.Other:
                    msgtosend += " \ud83d\udccc \n";
                    msgtosend += $"▹{Title} : {ReporterName} ({ReporterID}) \n";

                    break;

                case TicketType.Arkan:

                    msgtosend += " \ud83d\udca5 \n";
                    msgtosend += $"▹Arkan Detection for : {ReporterName} ({ReporterID}) \n";

                    // if(AType == ArkanType.Aim)
                    // {
                    //     var aim = _report._aimData;
                    //     msgtosend += $"▹Aim Hack";
                    //     msgtosend += $"▹TargetPlayer:  {aim.hitInfoHitEntityPlayerName} ({aim.hitInfoHitEntityPlayerUserID}) \n";
                    //     msgtosend += $"▹Hit Entity: {aim.bodyPart} \n";
                    //     msgtosend += $"▹At Distance: {aim.hitInfoProjectileDistance} \n";
                    //     msgtosend += $"▹With Weapon: {aim.weaponShortName} \n ";
                    //     msgtosend += $"▹Damage:  {aim.damage}";
                    //
                    // }
                    // else if (AType == ArkanType.Recoil)
                    // {
                    //     var rec = _report._norecoildata;
                    //     msgtosend += $"▹No Recoil Hack";
                    //     msgtosend += $"▹Violation Probability: {rec.violationProbability}% \n";
                    //     msgtosend += $"▹Shot Count: {rec.ShotsCnt}\n ";
                    //     msgtosend += $"▹Weapon: {rec.weaponShortName}\n";
                    //
                    // }

                    break;
            }

            return msgtosend;
        }

        public void ParseTicketInfo(Ticket newticket)
        {
            if (ID == newticket.ID)
            {
                Type = newticket.Type;
                Title = newticket.Title;
                ReporterID = newticket.ReporterID;
                ReporterName = newticket.ReporterName;
                ReporterPos = newticket.ReporterPos;
                TargetPos = newticket.TargetPos;
                TicketProcessed = newticket.TicketProcessed;
                TargetID = newticket.TargetID;
                server = newticket.server;
                Message = newticket.Message;
                TargetName = newticket.TargetName;
            }
        }

        public void SetArkanTicket()
        {
            //this._report = new ArkanReport();
            if (Message.Contains("NRViolationsCnt"))
                AType = ArkanType.Recoil;
            //   _report._norecoildata = JsonConvert.DeserializeObject<NoRecoilViolationData>(this.Message);
            else if (Message.Contains("projectileID"))
                AType = ArkanType.Aim;
            //  _report._aimData = JsonConvert.DeserializeObject<AIMViolationData>(this.Message);
            else
                AType = ArkanType.Rock;
            //_report._rockdata = JsonConvert.DeserializeObject<InRockViolationsData>(this.Message);
        }
    }
}