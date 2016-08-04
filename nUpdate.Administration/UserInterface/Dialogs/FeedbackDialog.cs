﻿// Author: Dominic Beger (Trade/ProgTrade)

using System;
using System.Drawing;
using System.Net.Mail;
using nUpdate.Administration.UserInterface.Popups;

namespace nUpdate.Administration.UserInterface.Dialogs
{
    internal partial class FeedbackDialog : BaseDialog
    {
        public FeedbackDialog()
        {
            InitializeComponent();
        }

        /// <summary>
        ///     Checks if the adress is a valid e-mail address.
        /// </summary>
        /// <param name="address">The adress to check.</param>
        /// <returns>Returns true if the mail address is valid.</returns>
        public bool IsValidMailAdress(string address)
        {
            try
            {
                // ReSharper disable once ObjectCreationAsStatement
                new MailAddress(address);
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        private void sendButton_Click(object sender, EventArgs e)
        {
            if (!ValidationManager.Validate(this))
            {
                Popup.ShowPopup(this, SystemIcons.Error, "Missing information", "All fields need to have a value.",
                    PopupButtons.Ok);
                return;
            }

            if (!IsValidMailAdress(emailTextBox.Text))
            {
                Popup.ShowPopup(SystemIcons.Error, "Invalid e-mail address", "Please enter a valid e-mail address.",
                    PopupButtons.Ok);
                return;
            }

            try
            {
                string responseString;
                using (var client = new WebClientEx())
                {
                    responseString =
                        client.DownloadString(
                            $"http://www.nupdate.net/mail.php?name={nameTextBox.Text}&sender={emailTextBox.Text}&content={contentTextBox.Text}");
                }

                if (!string.IsNullOrEmpty(responseString) && responseString != "\n")
                {
                    Popup.ShowPopup(this, SystemIcons.Error, "Error while sending feedback.",
                        $"Please report this message: {responseString}", PopupButtons.Ok);
                    return;
                }

                Popup.ShowPopup(this, SystemIcons.Information, "Delivering successful.",
                    "The feedback was sent. Thank you!", PopupButtons.Ok);
                Close();
            }
            catch (Exception ex)
            {
                Popup.ShowPopup(this, SystemIcons.Error, "Error while sending feedback.", ex, PopupButtons.Ok);
            }
        }

        private void FeedbackDialog_Load(object sender, EventArgs e)
        {
            Text = string.Format(Text, Program.VersionString);
        }
    }
}