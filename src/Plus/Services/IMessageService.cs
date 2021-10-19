﻿//*********************************************************************
//CAD+ Toolset
//Copyright(C) 2021 Xarial Pty Limited
//Product URL: https://cadplus.xarial.com
//License: https://cadplus.xarial.com/license/
//*********************************************************************

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xarial.XToolkit.Reporting;

namespace Xarial.CadPlus.Plus.Services
{
    public enum MessageServiceIcon_e 
    {
        Information,
        Warning,
        Error,
        Question
    }

    public enum MessageServiceButtons_e 
    {
        Ok,
        OkCancel,
        YesNo,
        YesNoCancel
    }

    public interface IMessageService
    {
        /// <summary>
        /// Type of exceptions which should be considered as user friendly errors
        /// </summary>
        Type[] UserErrors { get; }

        /// <summary>
        /// Shows message
        /// </summary>
        /// <param name="msg">Message to show</param>
        /// <param name="icon">Message box icon</param>
        /// <param name="btns">Message box buttons</param>
        /// <returns>True for Ok or Yes, False for No, null for Cancel</returns>
        bool? ShowMessage(string msg, MessageServiceIcon_e icon, MessageServiceButtons_e btns);
    }

    public static class IMessageServiceExtension
    {
        public static void ShowError(this IMessageService msgSvc, string error)
            => msgSvc.ShowMessage(error, MessageServiceIcon_e.Error, MessageServiceButtons_e.Ok);

        public static void ShowWarning(this IMessageService msgSvc, string warn)
            => msgSvc.ShowMessage(warn, MessageServiceIcon_e.Warning, MessageServiceButtons_e.Ok);

        public static void ShowInformation(this IMessageService msgSvc, string msg)
            => msgSvc.ShowMessage(msg, MessageServiceIcon_e.Information, MessageServiceButtons_e.Ok);

        public static bool? ShowQuestion(this IMessageService msgSvc, string question)
            => msgSvc.ShowMessage(question, MessageServiceIcon_e.Question, MessageServiceButtons_e.YesNoCancel);

        public static void ShowError(this IMessageService msgSvc, Exception ex, string baseMsg = "Generic error. Please see log for more details")
        {
            var err = ex.ParseUserError(out _, baseMsg, msgSvc.UserErrors ?? new Type[0]);
            msgSvc.ShowError(err);
        }
    }
}
