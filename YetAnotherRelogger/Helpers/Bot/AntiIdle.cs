﻿using System;
using System.Diagnostics;
using YetAnotherRelogger.Helpers.Attributes;
using YetAnotherRelogger.Helpers.Enums;
using YetAnotherRelogger.Helpers.Tools;
using YetAnotherRelogger.Properties;

namespace YetAnotherRelogger.Helpers.Bot
{
    public class AntiIdleClass
    {
        public double Delayed;
        public int Failed;
        public int FailedInitCount;
        public int FailedStartDelay;
        public int FixAttempts;
        public int InitAttempts;
        public DateTime InitTime;
        public bool IsInitialized;

        [NoCopy]
        public long LastCoinage;
        public DateTime LastCoinageBugReported;
        public DateTime LastCoinageIncrease;
        public DateTime LastCoinageReset; // So we give it a minute to get in shape
        [NoCopy]
        public long LastExperience;
        public DateTime LastExperienceBugReported;
        public DateTime LastExperienceIncrease;
        public DateTime LastExperienceReset; // So we give it a minute to get in shape
        public DateTime LastStats;
        [NoCopy] public Bot Parent;
        public DateTime StartDelay;
        public IdleState State;
        [NoCopy]
        public BotStats Stats;
        public DateTime TimeFailedStartDelay;
        private DateTime _fixAttemptTime;

        private DateTime _lastIdleAction;

        public AntiIdleClass()
        {
            FixAttempts = 0;
            Stats = new BotStats();
            ResetCoinage();
        }

        public BotCommand IdleAction
        {
            get
            {
                if (Program.Pause)
                    return BotCommand.Null;

                var debugStats =
                    $"STATS: LastRun:{General.DateSubtract(Stats.LastRun):0.00} LastGame:{General.DateSubtract(Stats.LastGame):0.00} LastPulse:{General.DateSubtract(Stats.LastPulse):0.00} IsRunning:{Stats.IsRunning} IsPaused:{Stats.IsPaused} IsInGame:{Stats.IsInGame}";
                Debug.WriteLine(debugStats);
                //Logger.Instance.Write(debugStats);
                if (Settings.Default.StartBotIfStopped && !Stats.IsRunning && General.DateSubtract(Stats.LastRun) > 90)
                {
                    if (!FixAttemptCounter())
                        return BotCommand.Null;
                    Logger.Instance.Write(Parent, "Demonbuddy:{0}: is stopped to long for a unknown reason (90 seconds)",
                        Parent.Demonbuddy.Proc.Id);
                    return BotCommand.Restart;
                }
                if (Settings.Default.StartBotIfStopped && Stats.IsPaused && General.DateSubtract(Stats.LastRun) > 90)
                {
                    if (!FixAttemptCounter())
                        return BotCommand.Null;
                    Logger.Instance.Write(Parent, "Demonbuddy:{0}: is paused to long (90 seconds)",
                        Parent.Demonbuddy.Proc.Id);
                    State = IdleState.Terminate;
                    return BotCommand.Null;
                }
                if (Settings.Default.AllowPulseFix && !Stats.IsPaused && General.DateSubtract(Stats.LastPulse) > 120)
                {
                    if (!FixAttemptCounter())
                        return BotCommand.Null;
                    Logger.Instance.Write(Parent, "Demonbuddy:{0}: is not pulsing while it should (120 seconds)",
                        Parent.Demonbuddy.Proc.Id);
                    return BotCommand.FixPulse;
                }
                if (Settings.Default.StartBotIfStopped && !Stats.IsInGame && General.DateSubtract(Stats.LastGame) > 90)
                {
                    if (!FixAttemptCounter())
                        return BotCommand.Null;
                    Logger.Instance.Write(Parent,
                        "Demonbuddy:{0}: is not in a game to long for unkown reason (90 seconds)",
                        Parent.Demonbuddy.Proc.Id);
                    return BotCommand.Restart;
                }

                // Prints a warning about gold error
                if (Settings.Default.GoldInfoLogging && General.DateSubtract(LastCoinageIncrease) > 60)
                {
                    if (General.DateSubtract(LastCoinageBugReported) > 60)
                    {
                        if (Settings.Default.UseGoldTimer)
                            Logger.Instance.Write(Parent,
                                "Demonbuddy:{0}: has not gained any gold in {1} seconds, limit {2}",
                                Parent.Demonbuddy.Proc.Id, (int) General.DateSubtract(LastCoinageIncrease),
                                (int) Settings.Default.GoldTimer);
                        else
                            Logger.Instance.Write(Parent,
                                "Demonbuddy:{0}: has not gained any gold in {1} seconds, limit NONE",
                                Parent.Demonbuddy.Proc.Id, (int) General.DateSubtract(LastCoinageIncrease));
                        LastCoinageBugReported = DateTime.UtcNow;
                    }
                }

                // If we are w/o gold change for 2 minutes, send reset, but at max every 45s
                if (Settings.Default.UseGoldTimer &&
                    General.DateSubtract(LastCoinageIncrease) > (double) Settings.Default.GoldTimer)
                {
                    if (General.DateSubtract(LastCoinageReset) < 45) // we still give it a chance
                        return BotCommand.Null;
                    // When we give up, it sends false, we send Roger and kill DB
                    if (!FixAttemptCounter())
                        return BotCommand.Null;
                    Logger.Instance.Write(Parent, "Demonbuddy:{0}: has not gained any gold in {1} seconds, trying reset",
                        Parent.Demonbuddy.Proc.Id,
                        (int) General.DateSubtract(LastCoinageIncrease));
                    LastCoinageReset = DateTime.UtcNow;
                    return BotCommand.Restart;
                }

                return BotCommand.Null;
            }
        }

        // When program is paused, we don't want this to run on and on
        public void ResetCoinage()
        {
            LastCoinageIncrease = DateTime.UtcNow;
            LastCoinageBugReported = DateTime.UtcNow;
            LastCoinage = 0;
            LastCoinageReset = DateTime.MinValue;
        }

        public void UpdateCoinage(long newCoinage)
        {
            if (newCoinage < 0)
            {
                Debug.WriteLine("We could not read Coinage assuming problem");
                return;
            }
            if (newCoinage == 0)
            {
                Debug.WriteLine("We got 0 gold, which is often a glitch, assuming problem");
                return;
            }

            Debug.WriteLine("We received Coinage info! Old: {0}; New {1}, we are {2}",
                LastCoinage, newCoinage, (LastCoinage != newCoinage) ? "good" : "lazy");

            if (newCoinage < LastCoinage)
            {
                // We either repaired, or went shopping, all is well
                LastCoinageIncrease = DateTime.UtcNow;
            }
            else if (newCoinage > LastCoinage)
            {
                // We got more monies, all is well
                LastCoinageIncrease = DateTime.UtcNow;
                LastCoinageBugReported = DateTime.UtcNow;
            }
            // Otherwise we are stuck on the same gold, and that's not profitable.
            // Yes, the if above could be: NewCoinage != LastCoinage, but I wanted
            // to explain why we have those two
            LastCoinage = newCoinage;
        }

        public BotCommand Reply()
        {
            if (Program.Pause)
                return BotCommand.Null;

            switch (State)
            {
                case IdleState.StartDelay:
                    if (Stats.IsInGame || Stats.IsLoadingWorld)
                    {
                        State = IdleState.CheckIdle;
                    }
                    else if (General.DateSubtract(StartDelay) > 0)
                    {
                        if (FailedStartDelay > 5 ||
                            (FailedStartDelay > 3 && General.DateSubtract(TimeFailedStartDelay) > 600))
                        {
                            State = IdleState.Terminate;
                            return BotCommand.Shutdown;
                        }
                        Logger.Instance.Write(Parent, "Demonbuddy:{0}: Delayed start failed! ({1} seconds overtime)",
                            Parent.Demonbuddy.Proc.Id, General.DateSubtract(StartDelay));
                        TimeFailedStartDelay = DateTime.UtcNow;
                        FailedStartDelay++;
                        return BotCommand.Restart;
                    }
                    break;
                case IdleState.CheckIdle:
                    _lastIdleAction = DateTime.UtcNow; // Update Last Idle action time
                    var idleAction = IdleAction;
                    if (idleAction != BotCommand.Null)
                        Logger.Instance.Write("Idle action: {0}", idleAction);
                    return idleAction;
                case IdleState.Busy:
                    if (Stats.IsRunning && !Stats.IsPaused && Stats.IsInGame)
                    {
                        Reset();
                    }
                    else if (General.DateSubtract(_lastIdleAction) > 10)
                    {
                        if (Failed >= 3)
                            State = IdleState.Terminate;

                        Failed++;
                        Reset();
                    }
                    break;
                case IdleState.UserStop:
                    if (Stats.IsRunning)
                        State = IdleState.CheckIdle;
                    ResetCoinage();
                    break;
                case IdleState.UserPause:
                    if (!Stats.IsPaused)
                    {
                        Reset();
                        State = IdleState.CheckIdle;
                    }
                    break;
                case IdleState.NewProfile:
                    State = IdleState.CheckIdle;
                    break;
                case IdleState.Terminate:
                    Parent.Restart();
                    return BotCommand.Shutdown;
            }
            return BotCommand.Null;
        }

        public bool FixAttemptCounter()
        {
            if (Program.Pause)
            {
                FixAttempts = 0;
                return true;
            }

            if (General.DateSubtract(_fixAttemptTime) > 420)
                FixAttempts = 0;

            Logger.Instance.Write("Fix Attempt++");
            FixAttempts++;
            _fixAttemptTime = DateTime.UtcNow;
            if (FixAttempts > 3)
            {
                //Parent.Stop();
                Logger.Instance.Write("Too many fix attempts, restarting bot");
                FixAttempts = 0;
                Parent.Restart();
                return false;
            }
            return true;
        }

        public void Reset(bool all = false, bool freshstart = false)
        {
            State = IdleState.CheckIdle;
            Stats.Reset();

            ResetCoinage();

            if (all)
            {
                IsInitialized = false;
                InitTime = DateTime.UtcNow;
                State = IdleState.Initialize;
                Failed = 0;
                FailedStartDelay = 0;
            }
            if (freshstart)
            {
                FailedInitCount = 0;
                FixAttempts = 0;
            }
        }
    }
}
