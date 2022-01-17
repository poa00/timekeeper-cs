﻿using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;
using Timekeeper.Client.Model;
using Timekeeper.Client.Model.Polls;
using Timekeeper.DataModel;

namespace Timekeeper.Client.Pages
{
    public partial class DisplayPolls
    {
        public PollGuest Handler
        {
            get;
            private set;
        }

        public string GetPollClass(Poll poll)
        {
            if (poll.IsVotingOpen)
            {
                return "poll-published";
            }

            return "poll-closed";
        }

        public MarkupString GetMarkup(string html)
        {
            return new MarkupString(html);
        }

        public bool ShowNoSessionMessage
        {
            get;
            private set;
        }

        public MobileHandler Mobile
        {
            get;
            private set;
        }

        [Parameter]
        public string SessionId
        {
            get;
            set;
        }

        protected override async Task OnInitializedAsync()
        {
            Log.LogInformation("-> OnInitializedAsync");

            if (string.IsNullOrEmpty(SessionId))
            {
                ShowNoSessionMessage = true;
            }
            else
            {
                var success = Guid.TryParse(SessionId, out Guid guid);

                if (!success
                    || guid == Guid.Empty)
                {
                    ShowNoSessionMessage = true;
                }
                else
                {
                    Handler = new PollGuest(
                        Config,
                        LocalStorage,
                        Log,
                        Http,
                        SessionId,
                        Session);

                    Handler.UpdateUi += HandlerUpdateUi;
                    await Handler.Connect();

                    Mobile = await new MobileHandler().Initialize(JSRuntime);
                }
            }

            Log.LogInformation("OnInitializedAsync ->");
        }

        private void HandlerUpdateUi(object sender, EventArgs e)
        {
            StateHasChanged();
        }

        public async void Dispose()
        {
            Log.LogTrace("Dispose");

            if (Handler != null)
            {
                Handler.UpdateUi -= HandlerUpdateUi;
            }

            await Task.Run(async () =>
            {
                await Handler.Disconnect();
            });
        }
    }
}