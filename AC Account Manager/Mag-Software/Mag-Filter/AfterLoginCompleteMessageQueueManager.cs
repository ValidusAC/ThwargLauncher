﻿using System;
using System.Collections.Generic;

using Mag.Shared;

using Decal.Adapter;

namespace MagFilter
{
    class AfterLoginCompleteMessageQueueManager
    {
        bool freshLogin;

        LoginCommands _loginCommands = new LoginCommands();
        bool sendingLastEnter;

        DateTime loginCompleteTime = DateTime.MaxValue;

        public void FilterCore_ClientDispatch(object sender, NetworkMessageEventArgs e)
        {
            if (e.Message.Type == 0xF7C8) // Enter Game
                freshLogin = true;

            if (freshLogin && e.Message.Type == 0xF7B1 && Convert.ToInt32(e.Message["action"]) == 0xA1) // Character Materialize (Any time is done portalling in, login or portal)
            {
                freshLogin = false;

                var persister = new LoginCommandPersister();
                _loginCommands = persister.ReadQueue();

                if (_loginCommands.MessageQueue.Count > 0)
                {
                    loginCompleteTime = DateTime.Now;

                    sendingLastEnter = false;
                    CoreManager.Current.RenderFrame += new EventHandler<EventArgs>(Current_RenderFrame);
                }
            }
        }

        void Current_RenderFrame(object sender, EventArgs e)
        {
            try
            {
                if (DateTime.Now.Subtract(TimeSpan.FromMilliseconds(_loginCommands.WaitMillisencds)) < loginCompleteTime)
                    return;

                if (_loginCommands.MessageQueue.Count == 0 && sendingLastEnter == false)
                {
                    CoreManager.Current.RenderFrame -= new EventHandler<EventArgs>(Current_RenderFrame);
                    return;
                }

                if (sendingLastEnter)
                {
                    PostMessageTools.SendEnter();
                    sendingLastEnter = false;
                }
                else
                {
                    PostMessageTools.SendEnter();
                    string cmd = _loginCommands.MessageQueue.Dequeue();
                    // The game is losing the first character of our commands
                    // So deliberately send a space at the start
                    if (!cmd.StartsWith(" "))
                    {
                        cmd = " " + cmd;
                    }
                    log.WriteInfo(String.Format("Dequeued a login message: '{0}'", cmd));
                    PostMessageTools.SendCharString(cmd);
                    sendingLastEnter = true;
                }
            }
            catch (Exception ex) { Debug.LogException(ex); }
        }

        private string TextRemainder(string text, string prefix)
        {
            if (text.Length <= prefix.Length) { return string.Empty; }
            return text.Substring(prefix.Length);
        }
        public void FilterCore_CommandLineText(object sender, ChatParserInterceptEventArgs e)
        {
            bool writeChanges = true;
            bool global = false;
            if (e.Text.Contains("/mfglobal")) { global = true; }
            log.WriteDebug(string.Format("FilterCore_CommandLineText: '{0}'", e.Text));
            if (e.Text.StartsWith("/mf log "))
            {
                string logmsg = TextRemainder(e.Text, "/mf log ");
                log.WriteInfo(logmsg);

                e.Eat = true;
            }
            else if (e.Text.StartsWith("/mf alcmq add ") || e.Text.StartsWith("/mf olcmq add "))
            {
                string cmd = TextRemainder(e.Text, "/mf alcmq add ");
                _loginCommands.MessageQueue.Enqueue(cmd);
                Debug.WriteToChat("After Login Complete Message Queue added: " + cmd);

                e.Eat = true;
            }
            else if (e.Text == "/mf alcmq clear" || e.Text == "/mf olcmq clear")
            {
                _loginCommands.MessageQueue.Clear();
                Debug.WriteToChat("After Login Complete Message Queue cleared");

                e.Eat = true;
            }
            else if (e.Text.StartsWith("/mf alcmq wait set "))
            {
                string valstr = TextRemainder(e.Text, "/mf alcmq wait set ");
                _loginCommands.WaitMillisencds = int.Parse(valstr);
                Debug.WriteToChat("After Login Complete Message Queue Wait time set: " + valstr + "ms");

                e.Eat = true;
            }
            else if (e.Text.StartsWith("/mf olcwait set ")) // Backwards Compatability
            {
                string valstr = TextRemainder(e.Text, "/mf olcwait set ");
                _loginCommands.WaitMillisencds = int.Parse(valstr);
                Debug.WriteToChat("After Login Complete Message Queue Wait time set: " + valstr + "ms");

                e.Eat = true;
            }
            else if (e.Text == "/mf alcmq wait clear" || e.Text == "/mf olcwait clear")
            {
                _loginCommands.ClearWait();
                Debug.WriteToChat(string.Format("After Login Complete Wait time reset to default {0} ms", LoginCommands.DefaultMillisecondsToWaitAfterLoginComplete));

                e.Eat = true;
            }
            else if (e.Text == "/mf alcmq show" || e.Text == "/mf olcmq show")
            {
                Debug.WriteToChat(string.Format("LoginCmds: {0}", _loginCommands.MessageQueue.Count));
                foreach (string cmd in _loginCommands.MessageQueue)
                {
                    Debug.WriteToChat(string.Format("cmd: {0}", cmd));
                }
                Debug.WriteToChat(string.Format("Wait: {0}", _loginCommands.WaitMillisencds));

                e.Eat = true;
                writeChanges = false;
            }
            if (e.Eat && writeChanges)
            {
                var persister = new LoginCommandPersister();
                persister.WriteQueue(_loginCommands, global);
            }
        }
    }
}