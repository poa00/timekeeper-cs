﻿using Microsoft.AspNetCore.Components;
using System;
using System.Threading.Tasks;
using TimekeeperClient.Model;

namespace TimekeeperClient.Pages
{
    public partial class Guest : IDisposable
    {
        [Parameter]
        public string Session
        {
            get;
            set;
        }

        public SignalRGuest Handler
        {
            get;
            private set;
        }

        private void HandlerUpdateUi(object sender, EventArgs e)
        {
            StateHasChanged();
        }

        protected override async Task OnInitializedAsync()
        {
            Handler = new SignalRGuest(
                Config,
                LocalStorage,
                Log,
                Http,
                Session);

            Handler.UpdateUi += HandlerUpdateUi;
            await Handler.Connect();
        }

        public void Dispose()
        {
            if (Handler != null)
            {
                Handler.UpdateUi -= HandlerUpdateUi;
            }
        }
    }
}