﻿using Blazored.LocalStorage;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Timekeeper.DataModel;

namespace Timekeeper.Client.Model.Polls
{
    public class PollHost : SignalRHostBase
    {
        private string _sessionId;

        public PollHost(
            IConfiguration config,
            ILocalStorageService localStorage,
            ILogger log,
            HttpClient http,
            NavigationManager nav,
            SessionHandler session,
            string sessionId) : base(config, localStorage, log, http, nav, session)
        {
            _sessionId = sessionId;
        }

        public override async Task Connect()
        {
            _log.LogInformation("-> PollHost.Connect");

            IsBusy = true;

            var ok = await InitializeSession(_sessionId)
                && await InitializePeerInfo()
                && await CreateConnection();

            if (ok)
            {
                _connection.On<string>(Constants.PublishPollMessage, p => ReceivePublishUnpublishPoll(p, true));
                _connection.On<string>(Constants.UnpublishPollMessage, p => ReceivePublishUnpublishPoll(p, false));
                _connection.On<string>(Constants.VotePollMessage, p => ReceiveVote(p));
                _connection.On<string>(Constants.RequestPollsMessage, SendPolls);
                _connection.On<string>(Constants.MovePollMessage, MovePoll);

                ok = await StartConnection();
            }

            if (ok)
            {
                _log.LogTrace("CreateConnection and StartConnection OK");
                IsConnected = true;
            }
            else
            {
                _log.LogTrace("StartConnection NOT OK");
                IsConnected = false;
                IsInError = true;
                ErrorStatus = "Error";
                RaiseUpdateEvent();
                return;
            }

            await SendPolls();

            Status = "Connected";
            IsBusy = false;
            _log.LogInformation("PollsHost.Connect ->");
        }

        private void MovePoll(string json)
        {
            _log.LogTrace("HIGHLIGHT---> MovePoll");

            var message = JsonConvert.DeserializeObject<MovePollMessage>(json);
            var poll = CurrentSession.Polls.FirstOrDefault(p => p.Uid == message.Uid);

            if (poll == null)
            {
                _log.LogDebug($"No such poll: {message.Uid}");
                return;
            }

            var index = CurrentSession.Polls.IndexOf(poll);

            if (index == message.NewIndex)
            {
                _log.LogDebug($"Poll is already at index {message.NewIndex}");
                return;
            }

            CurrentSession.Polls.Remove(poll);
            CurrentSession.Polls.Insert(message.NewIndex, poll);
            RaiseUpdateEvent();
        }

        private async Task SendPolls()
        {
            await SendPolls(string.Empty);
        }

        private async Task SendPolls(string _)
        {
            _log.LogTrace("HIGHLIGHT---> SendPolls");

            var publishedPolls = CurrentSession.Polls
                .Where(p => p.IsPublished);

            if (publishedPolls.Count() == 0)
            {
                return;
            }

            var list = new ListOfPolls
            {
                CustomTitle = CurrentSession.SessionName
            };

            list.Polls.AddRange(publishedPolls);

            _log.LogDebug($"HIGHLIGHT--CustomTitle: {list.CustomTitle}");

            var json = JsonConvert.SerializeObject(list);

            _log.LogDebug($"HIGHLIGHT--json: {json}");

            var pollsUrl = $"{_hostName}/polls";
            var httpRequest = new HttpRequestMessage(HttpMethod.Post, pollsUrl);
            httpRequest.Headers.Add(Constants.GroupIdHeaderKey, CurrentSession.SessionId);
            httpRequest.Content = new StringContent(json);

            var response = await _http.SendAsync(httpRequest);

            if (!response.IsSuccessStatusCode)
            {
                // TODO Handle failure
            }
        }

        private async Task ReceiveVote(string pollJson)
        {
            Poll receivedPoll;

            try
            {
                receivedPoll = JsonConvert.DeserializeObject<Poll>(pollJson);
            }
            catch
            {
                _log.LogTrace("Error with received poll");
                return;
            }

            var poll = CurrentSession.Polls.FirstOrDefault(p => p.Uid == receivedPoll.Uid);

            if (poll == null)
            {
                _log.LogDebug($"Poll doesn't exist: {receivedPoll.Uid}");
                return;
            }

            if (!poll.IsVotingOpen)
            {
                _log.LogDebug($"Voting is closed for poll {receivedPoll.Uid}");
                return;
            }

            var chosenAnswer = poll.Answers
                .FirstOrDefault(a => a.Letter == receivedPoll.GivenAnswer);

            if (chosenAnswer == null)
            {
                _log.LogDebug($"No such question {receivedPoll.Uid} / receivedPoll.GivenAnswer");
                return;
            }

            chosenAnswer.Count++;

            double totalCount = 0;

            foreach (var answer in poll.Answers)
            {
                totalCount += answer.Count;
            }

            foreach (var answer in poll.Answers)
            {
                answer.Ratio = (answer.Count / totalCount);
            }

            poll.GivenAnswer = receivedPoll.GivenAnswer;

            await SaveSession();

            RaiseUpdateEvent();
        }

        public bool IsAnyPollPublished
        {
            get => CurrentSession.Polls.Any(p => p.IsPublished);
        }

        public async Task MovePollUpDown(string uid, bool up)
        {
            _log.LogTrace("-> MovePollUpDown");
            _log.LogDebug($"{CurrentSession.Polls.Count} polls in collection");

            var poll = CurrentSession.Polls.FirstOrDefault(p => p.Uid == uid);

            if (poll == null)
            {
                _log.LogTrace($"Cannot find poll: {poll.Uid}");
                return;
            }

            var move = up ? -1 : +1;
            var newIndex = CurrentSession.Polls.IndexOf(poll) + move;

            if ((newIndex < 0)
                || (newIndex >= CurrentSession.Polls.Count))
            {
                _log.LogTrace($"Cannot move poll: {poll.Uid}");
                return;
            }

            CurrentSession.Polls.Remove(poll);
            CurrentSession.Polls.Insert(newIndex, poll);
            await SaveSession();
            RaiseUpdateEvent();

            var movePollMessage = new MovePollMessage
            {
                Uid = poll.Uid,
                NewIndex = move
            };

            var json = JsonConvert.SerializeObject(movePollMessage);

            var movePollUrl = $"{_hostName}/move-poll";
            var httpRequest = new HttpRequestMessage(HttpMethod.Post, movePollUrl);
            httpRequest.Headers.Add(Constants.GroupIdHeaderKey, CurrentSession.SessionId);
            httpRequest.Content = new StringContent(json);

            var response = await _http.SendAsync(httpRequest);

            if (!response.IsSuccessStatusCode)
            {
                // TODO Handle failure
            }

            _log.LogTrace("MovePollUpDown ->");
        }

        public async Task ReceivePublishUnpublishPoll(string pollJson, bool mustPublish)
        {
            _log.LogTrace($"-> {nameof(ReceivePublishUnpublishPoll)} {mustPublish}");

            Poll receivedPoll;

            try
            {
                receivedPoll = JsonConvert.DeserializeObject<Poll>(pollJson);
            }
            catch
            {
                _log.LogTrace("Error with received poll");
                return;
            }

            var poll = CurrentSession.Polls.FirstOrDefault(p => p.Uid == receivedPoll.Uid);

            if (poll == null)
            {
                _log.LogTrace($"Poll doesn't exist: {receivedPoll.Uid}");
                return;
            }

            poll.IsVotingOpen = receivedPoll.IsVotingOpen;
            poll.IsPublished = mustPublish;

            if (!poll.IsPublished)
            {
                poll.IsVotingOpen = false;
            }

            RaiseUpdateEvent();

            if (poll.IsBroadcasting)
            {
                _log.LogTrace($"Poll is currently broadcasting");
                return;
            }

            if ((poll.IsPublished && mustPublish)
                || (!poll.IsPublished && !mustPublish))
            {
                _log.LogTrace($"Poll is already in the correct state");
                return;
            }

            await _session.SaveToStorage(CurrentSession, SessionKey, _log);

            if (mustPublish)
            {
                Status = "Poll was published remotely";
            }
            else
            {
                Status = "Poll was unpublished remotely";
            }

            RaiseUpdateEvent();
        }

        public async Task<bool> OpenClosePoll(Poll poll, bool mustOpen)
        {
            if (poll == null
                || (poll.IsVotingOpen && mustOpen)
                || (!poll.IsVotingOpen && !mustOpen))
            {
                _log.LogTrace($"Poll is already in the correct state");
                return false;
            }

            poll.IsVotingOpen = mustOpen;
            return await DoPublishUnpublishPoll(poll, poll.IsPublished, mustOpen);
        }

        private async Task<bool> DoPublishUnpublishPoll(Poll poll, bool mustPublish, bool? mustOpen = null)
        {
            Status = "Attempting to publish poll";
            RaiseUpdateEvent();
            string publishUnpublishUrl;

            if (mustPublish)
            {
                publishUnpublishUrl = $"{_hostName}/publish-poll";
            }
            else
            {
                publishUnpublishUrl = $"{_hostName}/unpublish-poll";
            }

            _log.LogDebug($"publishUnpublishUrl: {publishUnpublishUrl}");

            var httpRequest = new HttpRequestMessage(HttpMethod.Post, publishUnpublishUrl);
            httpRequest.Headers.Add(Constants.GroupIdHeaderKey, CurrentSession.SessionId);

            var json = JsonConvert.SerializeObject(poll);
            httpRequest.Content = new StringContent(json);

            poll.IsBroadcasting = true;
            var response = await _http.SendAsync(httpRequest);

            if (!response.IsSuccessStatusCode)
            {
                _log.LogError($"Cannot send message: {response.ReasonPhrase}");
                poll.IsBroadcasting = false;
                ErrorStatus = "Error publishing poll";
            }
            else
            {
                if (mustPublish)
                {
                    Status = "Poll published";
                }
                else
                {
                    Status = "Poll unpublished";
                }

                poll.IsPublished = mustPublish;

                if (mustOpen != null)
                {
                    poll.IsVotingOpen = mustOpen.Value;
                }

                await _session.Save(CurrentSession, SessionKey, _log);
            }

            RaiseUpdateEvent();
            poll.IsBroadcasting = false;
            return response.IsSuccessStatusCode;
        }

        public async Task<bool> PublishUnpublishPoll(Poll poll, bool mustPublish)
        {
            _log.LogTrace($"-> {nameof(PublishUnpublishPoll)} {mustPublish}");

            if (poll == null
                || (poll.IsPublished && mustPublish)
                || (!poll.IsPublished && !mustPublish))
            {
                _log.LogTrace($"Poll is already in the correct state");
                return false;
            }

            poll.IsVotingOpen = mustPublish;

            var success = await DoPublishUnpublishPoll(poll, mustPublish);

            _log.LogTrace($"{nameof(PublishUnpublishPoll)} ->");
            return success;
        }

        public async Task<bool> InitializeSession(string sessionId)
        {
            _log.LogInformation("-> PollHost.InitializeSession");

            CurrentSession = await _session.GetFromStorage(SessionKey, _log);

            if (CurrentSession == null)
            {
                _log.LogDebug("Session in storage is Null");
                _nav.NavigateTo("/");
                return false;
            }
            else
            {
                _log.LogDebug($"SessionId in Storage: {CurrentSession.SessionId}");

                if (CurrentSession.SessionId != sessionId)
                {
                    _log.LogTrace("Session ID mismatch");
                    CurrentSession = null;
                    ErrorStatus = "Session ID mismatch";
                    IsOffline = false;
                    IsSessionMismatch = true;
                    RaiseUpdateEvent();
                    _log.LogTrace("Done informing user");
                    return false;
                }

                // Refresh session
                var sessions = await _session.GetSessions(_log);
                var outSession = sessions.FirstOrDefault(s => s.SessionId == CurrentSession.SessionId);

                _log.LogDebug($"outSession == null: {outSession == null}");

                CurrentSession = outSession;
            }

            RaiseUpdateEvent();

            _log.LogInformation("PollHost.InitializeSession ->");
            return true;
        }

        public bool IsSessionMismatch
        {
            get;
            private set;
        }
    }
}