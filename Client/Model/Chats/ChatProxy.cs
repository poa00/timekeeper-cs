﻿using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Timekeeper.DataModel;

namespace Timekeeper.Client.Model.Chats
{
    public class ChatProxy
    {
        public event EventHandler NewChatCreated;

        private string _hostNameFree;
        private HttpClient _http;

        public ChatProxy(
            HttpClient http,
            string hostNameFree)
        {
            _http = http;
            _hostNameFree = hostNameFree;
        }

        public Chat NewChat { get; set; }

        public bool IsSendingChat { get; set; }
        public bool IsInError { get; private set; }
        public string Status { get; private set; }
        public string ErrorStatus { get; private set; }

        public void SetNewChat()
        {
            NewChat = new Chat()
            {
                UniqueId = Guid.NewGuid().ToString(),
                CssClass = Constants.OwnChatCss,
                ContainerCssClass = Constants.OwnChatContainerCss
            };

            NewChatCreated?.Invoke(this, EventArgs.Empty);
        }

        public static void UpdateChats(
            SessionBase currentSession,
            string ownPeerId)
        {
            if (currentSession.Chats != null)
            {
                foreach (var chat in currentSession.Chats)
                {
                    if (chat.UserId == ownPeerId)
                    {
                        chat.CssClass = Constants.OwnChatCss;
                        chat.ContainerCssClass = Constants.OwnChatContainerCss;
                        chat.DisplayColor = Constants.OwnColor;
                    }
                    else
                    {
                        chat.CssClass = Constants.OtherChatCss;
                        chat.ContainerCssClass = Constants.OtherChatContainerCss;
                        chat.DisplayColor = chat.CustomColor;
                    }
                }
            }
        }

        public async Task<bool> SendCurrentChat(
            Action raiseUpdateEvent,
            PeerMessage peerInfoMessage,
            string sessionName,
            string sessionId,
            ILogger log)
        {
            IsSendingChat = true;
            raiseUpdateEvent();

            NewChat.UserId = peerInfoMessage.PeerId;
            NewChat.SenderName = peerInfoMessage.DisplayName;
            NewChat.MessageDateTime = DateTime.Now;
            NewChat.CustomColor = peerInfoMessage.ChatColor;

            var ok = await SendChats(
                new List<Chat>
                {
                    NewChat
                },
                sessionName,
                sessionId,
                log);

            SetNewChat();

            IsSendingChat = false;
            raiseUpdateEvent();

            return ok;
        }

        public async Task<bool> SendChats(
            IList<Chat> chats,
            string sessionName,
            string sessionId,
            ILogger log)
        {
            if (chats.Count == 0)
            {
                return true;
            }

            foreach (var chat in chats)
            {
                chat.SessionName = null;
            }

            chats.First().SessionName = sessionName;

            var list = new ListOfChats();
            list.Chats.AddRange(chats);

            var json = JsonConvert.SerializeObject(list);

            //_log.LogDebug($"json: {json}");

            var chatsUrl = $"{_hostNameFree}/chats";
            var httpRequest = new HttpRequestMessage(HttpMethod.Post, chatsUrl);
            httpRequest.Headers.Add(Constants.GroupIdHeaderKey, sessionId);
            httpRequest.Content = new StringContent(json);

            var response = await _http.SendAsync(httpRequest);

            if (!response.IsSuccessStatusCode)
            {
                IsInError = true;
                log.LogError($"Issue sending chat: {response.StatusCode} / {response.ReasonPhrase}");
                ErrorStatus = "Error connecting to Chat service, try to refresh the page and send again";
                return false;
            }

            IsInError = false;
            ErrorStatus = null;
            Status = "Chat sent";
            return true;
        }
    }
}