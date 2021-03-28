﻿using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using System;
using System.Threading.Tasks;
using Timekeeper.Client.Model;

namespace Timekeeper.Client.Pages
{
    public partial class Host : IDisposable
    {
        [Parameter]
        public string ResetSession
        {
            get;
            set;
        }

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            await JSRuntime.InvokeVoidAsync("branding.setTitle", Branding.WindowTitle);
        }

        public SignalRHost Handler
        {
            get;
            private set;
        }

        private void HandlerUpdateUi(object sender, EventArgs e)
        {
            StateHasChanged();
        }

        [CascadingParameter]
        private Task<AuthenticationState> AuthenticationStateTask
        {
            get;
            set;
        }

        protected override async Task OnInitializedAsync()
        {
            Log.LogInformation("-> Host.OnInitializedAsync");

#if !DEBUG
            if (Branding.MustAuthorize)
            {
                var authState = await AuthenticationStateTask;

                if (authState == null
                    || authState.User == null
                    || authState.User.Identity == null
                    || !authState.User.Identity.IsAuthenticated)
                {
                    Log.LogWarning("Unauthenticated");
                    return;
                }
            }
#endif

            Handler = new SignalRHost(
                Config,
                LocalStorage,
                Log,
                Http);

            if (Branding.MustAuthorize)
            {
                Log.LogTrace("HIGHLIGHT--Check authorization");
                await Handler.CheckAuthorize();

                if (Handler.IsAuthorized != null
                    && !Handler.IsAuthorized.Value)
                {
                    Log.LogError("No authorization");
                    return;
                }
            }

            if (ResetSession == "reset")
            {
                await Handler.DoDeleteSession();
                Nav.NavigateTo("/host", true);
                return;
            }

            Handler.UpdateUi += HandlerUpdateUi;
            await Handler.Connect(Branding.TemplateName);
            SessionName = Handler.CurrentSession.SessionName;
        }

        public string SessionName
        {
            get;
            private set;
        }

        public async void Dispose()
        {
            Log.LogTrace("Dispose");

            if (Handler != null)
            {
                Handler.UpdateUi -= HandlerUpdateUi;
                await Handler.Disconnect();
            }
        }
    }
}