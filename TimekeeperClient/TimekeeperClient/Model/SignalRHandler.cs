﻿using Blazored.LocalStorage;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Model;
using Newtonsoft.Json;
using System;
using System.Net.Http;
using System.Threading.Tasks;
using Timekeeper.DataModel;
using TimeKeeperApi.DataModel;

namespace TimekeeperClient.Model
{
    public abstract class SignalRHandler
    {
        public event EventHandler CountdownFinished;
        public event EventHandler UpdateUi;

        private string _clockDisplay;
        private string _errorStatus;
        private bool _isClockRunning;
        private string _status;
        protected const string FunctionCodeHeaderKey = "x-functions-key";
        protected const string HostNameKey = "HostName";
        protected const string NegotiateKeyKey = "NegotiateKey";
        protected const string SendMessageKeyKey = "SendMessageKey";
        protected const string StartClockKeyKey = "StartClockKey";
        protected const string StopClockKeyKey = "StopClockKey";
        protected const string RegisterKeyKey = "RegisterKey";
        protected const string UnregisterKeyKey = "UnregisterKey";
        protected IConfiguration _config;
        protected HubConnection _connection;

        public bool IsDisconnectDisabled 
        { 
            get; 
            protected set; 
        }

        public bool IsReconnectDisabled
        {
            get;
            protected set;
        }

        protected string _hostName;
        protected HttpClient _http;
        protected ILogger _log;

        public string ClockDisplay
        {
            get => _clockDisplay;
            protected set
            {
                _clockDisplay = value;
                RaiseUpdateEvent();
            }
        }

        public string CurrentMessage
        {
            get;
            protected set;
        }

        public string ErrorStatus
        {
            get => _errorStatus;
            protected set
            {
                _errorStatus = value;

                if (!string.IsNullOrEmpty(_errorStatus))
                {
                    Status = string.Empty;
                }
            }
        }

        public bool IsBusy
        {
            get;
            protected set;
        }

        public bool IsClockRunning
        {
            get => _isClockRunning;
            protected set
            {
                _isClockRunning = value;
                RaiseUpdateEvent();
            }
        }

        public bool IsConnected
        {
            get;
            protected set;
        }

        public bool IsInError
        {
            get;
            protected set;
        }

        public bool IsRed
        {
            get;
            protected set;
        }

        public bool IsYellow
        {
            get;
            protected set;
        }

        public string Status
        {
            get => _status;
            protected set
            {
                _status = value;

                if (!string.IsNullOrEmpty(_status))
                {
                    ErrorStatus = string.Empty;
                }
            }
        }

        public SignalRHandler(
            IConfiguration config,
            ILocalStorageService localStorage,
            ILogger log,
            HttpClient http)
        {
            CurrentMessage = "Welcome!";
            Status = "Please wait...";
            ClockDisplay = "00:00:00";

            _config = config;
            _localStorage = localStorage;
            _log = log;
            _http = http;
        }

        public Session CurrentSession
        {
            get;
            private set;
        }

        private readonly ILocalStorageService _localStorage;

        public async Task<bool> InitializeSession(
            string sessionId = null)
        {
            _log.LogInformation("HIGHLIGHT---> InitializeSession");
            _log.LogDebug($"HIGHLIGHT--sessionId: {sessionId}");

            if (string.IsNullOrEmpty(sessionId))
            {
                var json = await _localStorage.GetItemAsStringAsync(
                    Constants.SessionStorageKey);

                _log.LogDebug($"json: {json}");

                if (!string.IsNullOrEmpty(json))
                {
                    CurrentSession = JsonConvert.DeserializeObject<Session>(json);
                    _log.LogDebug($"HIGHLIGHT--Found CurrentSession.SessionId: {CurrentSession.SessionId}");
                }
            }

            if (CurrentSession == null)
            {
                _log.LogTrace("HIGHLIGHT--CurrentSession is null");

                CurrentSession = new Session
                {
                    SessionId = string.IsNullOrEmpty(sessionId) ? Guid.NewGuid().ToString() : sessionId,
                    SessionName = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),

                    ClockMessage = new StartClockMessage
                    {
                        CountDown = TimeSpan.FromMinutes(5), // TODO Make configurable
                        Red = TimeSpan.FromSeconds(30), // TODO Make configurable
                        Yellow = TimeSpan.FromSeconds(120), // TODO Make configurable
                        ServerTime = DateTime.Now
                    }
                };

                _log.LogDebug($"HIGHLIGHT--New CurrentSession.SessionId: {CurrentSession.SessionId}");
                
                await SaveCurrentSession();
            }

            _log.LogInformation("HIGHLIGHT--InitializeSession ->");
            return true;
        }

        public async Task SaveCurrentSession()
        {
            var json = JsonConvert.SerializeObject(CurrentSession);

            await _localStorage.SetItemAsync(
                Constants.SessionStorageKey,
                json);

            _log.LogTrace("HIGHLIGHT--Session saved to storage");
        }

        public async Task Disconnect()
        {
            _log.LogInformation("-> Disconnect");

            if (_connection != null)
            {
                await _connection.StopAsync();
                await _connection.DisposeAsync();
                _connection = null;
                _log.LogTrace("Connection is stopped and disposed");
            }

            CurrentSession = null;
            await _localStorage.RemoveItemAsync(Constants.SessionStorageKey);
            _log.LogTrace("CurrentSession is deleted");

            IsDisconnectDisabled = true;
            IsReconnectDisabled = false;
            Status = "Disconnected";

            _log.LogInformation("Disconnect ->");
        }

        private async Task<bool> RegisterToGroup()
        {
            try
            {
                var registerUrl = $"{_hostName}/register";
                _log.LogDebug($"registerUrl: {registerUrl}");

                var functionKey = _config.GetValue<string>(RegisterKeyKey);
                _log.LogDebug($"functionKey: {functionKey}");

                _log.LogDebug($"SessionId: {CurrentSession.SessionId}");
                _log.LogDebug($"UserId: {Program.GroupInfo.UserId}");

                var httpRequest = new HttpRequestMessage(HttpMethod.Post, registerUrl);
                httpRequest.Headers.Add(FunctionCodeHeaderKey, functionKey);
                httpRequest.Headers.Add(Constants.GroupIdHeaderKey, CurrentSession.SessionId);

                var registerInfo = new UserInfo
                {
                    UserId = Program.GroupInfo.UserId
                };

                var content = new StringContent(JsonConvert.SerializeObject(registerInfo));
                httpRequest.Content = content;

                var response = await _http.SendAsync(httpRequest);

                if (!response.IsSuccessStatusCode)
                {
                    _log.LogError($"Error registering for group: {response.ReasonPhrase}");
                    ErrorStatus = "Error with the backend, please contact support";
                    _log.LogInformation("SignalRHandler.CreateConnection ->");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _log.LogError($"Error reaching the function: {ex.Message}");
                ErrorStatus = "Error with the backend, please contact support";
                _log.LogInformation("SignalRHandler.CreateConnection ->");
                return false;
            }

            return true;
        }

        protected async Task<bool> CreateConnection()
        {
            _log.LogInformation("-> SignalRHandler.CreateConnection");

            _hostName = _config.GetValue<string>(HostNameKey);
            _log.LogDebug($"hostName: {_hostName}");

            NegotiateInfo negotiateInfo = null;

            try
            {
                var functionKey = _config.GetValue<string>(NegotiateKeyKey);
                _log.LogDebug($"functionKey: {functionKey}");

                var negotiateUrl = $"{_hostName}/negotiate";
                _log.LogDebug($"negotiateUrl: {negotiateUrl}");

                var httpRequest = new HttpRequestMessage(HttpMethod.Get, negotiateUrl);
                httpRequest.Headers.Add(FunctionCodeHeaderKey, functionKey);
                httpRequest.Headers.Add(Constants.UserIdHeaderKey, Program.GroupInfo.UserId);

                _log.LogDebug($"UserId: {Program.GroupInfo.UserId}");

                var response = await _http.SendAsync(httpRequest);

                if (response.IsSuccessStatusCode)
                {
                    var negotiateJson = await response.Content.ReadAsStringAsync();
                    negotiateInfo = JsonConvert.DeserializeObject<NegotiateInfo>(negotiateJson);
                }
                else
                {
                    _log.LogError($"Error reaching the function: {response.ReasonPhrase}");
                    ErrorStatus = "Error with the backend, please contact support";
                    _log.LogInformation("SignalRHandler.CreateConnection ->");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _log.LogError($"Error reaching the function: {ex.Message}");
                ErrorStatus = "Error with the backend, please contact support";
                _log.LogInformation("SignalRHandler.CreateConnection ->");
                return false;
            }

            _log.LogDebug($"HubName: {negotiateInfo.HubName}");

            try
            {
                _connection = new HubConnectionBuilder()
                    .WithUrl(negotiateInfo.Url, options =>
                    {
                        options.AccessTokenProvider = async () => negotiateInfo.AccessToken;
                    })
                    .WithAutomaticReconnect()
                    .Build();

                _connection.Reconnecting += ConnectionReconnecting;
                _connection.Reconnected += ConnectionReconnected;
            }
            catch (Exception ex)
            {
                _log.LogError($"Error reaching the SignalR service: {ex.Message}");
                ErrorStatus = "Error connecting, please contact support";
                return false;
            }

            var ok = await RegisterToGroup(); // TODO handle failure

            if (!ok)
            {
                return false;
            }

            Status = "Ready...";
            _log.LogInformation("SignalRHandler.CreateConnection ->");
            return true;
        }

        private Task ConnectionReconnected(string arg)
        {
            var tcs = new TaskCompletionSource<bool>();
            Status = "Reconnected!";
            tcs.SetResult(true);
            return tcs.Task;
        }

        private Task ConnectionReconnecting(Exception arg)
        {
            var tcs = new TaskCompletionSource<bool>();
            
            ErrorStatus = "Lost connection, trying to reconnect...";
            IsBusy = true;
            IsConnected = false;
            IsInError = true;


            tcs.SetResult(true);
            return tcs.Task;
        }

        protected virtual void DisplayMessage(string message)
        {
            _log.LogInformation("-> DisplayMessage");
            _log.LogDebug(message);

            CurrentMessage = message;
            RaiseUpdateEvent();
        }

        protected void RaiseUpdateEvent()
        {
            UpdateUi?.Invoke(this, EventArgs.Empty);
        }

        protected void RunClock()
        {
            IsClockRunning = true;
            Status = "Clock is running";

            Task.Run(async () =>
            {
                do
                {
                    if (IsClockRunning)
                    {
                        var elapsed = DateTime.Now - CurrentSession.ClockMessage.ServerTime;
                        var remains = CurrentSession.ClockMessage.CountDown - elapsed;

                        if (remains.TotalSeconds < 0)
                        {
                            IsClockRunning = false;
                            IsRed = false;
                            IsYellow = false;
                            Status = "Countdown finished";
                            ClockDisplay = "00:00:00";
                            CountdownFinished?.Invoke(this, EventArgs.Empty);
                            return;
                        }

                        ClockDisplay = remains.ToString(@"hh\:mm\:ss");

                        if (Math.Floor(remains.TotalSeconds) <= CurrentSession.ClockMessage.Red.TotalSeconds + 1)
                        {
                            IsRed = true;
                        }

                        if (Math.Floor(remains.TotalSeconds) <= CurrentSession.ClockMessage.Yellow.TotalSeconds + 1)
                        {
                            IsYellow = true;
                        }
                    }

                    await Task.Delay(1000);
                }
                while (IsClockRunning);
            });
        }

        protected async Task<bool> StartConnection()
        {
            _log.LogInformation("-> SignalRHandler.StartConnection");

            try
            {
                await _connection.StartAsync();
            }
            catch (Exception ex)
            {
                _log.LogError($"Error starting the connection: {ex.Message}");
                ErrorStatus = "Error connecting, please contact support";
                return false;
            }

            Status = "Connected!";

            _log.LogInformation("SignalRHandler.StartConnection ->");
            return true;
        }

        protected void StopClock(object _)
        {
            _log.LogInformation("-> StopClock");
            IsRed = false;
            IsYellow = false;
            Status = "Clock was stopped";
            IsClockRunning = false;
        }

        public abstract Task Connect();
    }
}