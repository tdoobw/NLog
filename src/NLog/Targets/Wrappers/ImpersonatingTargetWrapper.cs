// 
// Copyright (c) 2004-2018 Jaroslaw Kowalski <jaak@jkowalski.net>, Kim Christensen, Julian Verdurmen
// 
// All rights reserved.
// 
// Redistribution and use in source and binary forms, with or without 
// modification, are permitted provided that the following conditions 
// are met:
// 
// * Redistributions of source code must retain the above copyright notice, 
//   this list of conditions and the following disclaimer. 
// 
// * Redistributions in binary form must reproduce the above copyright notice,
//   this list of conditions and the following disclaimer in the documentation
//   and/or other materials provided with the distribution. 
// 
// * Neither the name of Jaroslaw Kowalski nor the names of its 
//   contributors may be used to endorse or promote products derived from this
//   software without specific prior written permission. 
// 
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
// AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE 
// IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE 
// ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE 
// LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR 
// CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF
// SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS 
// INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN 
// CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) 
// ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF 
// THE POSSIBILITY OF SUCH DAMAGE.
// 

#if !SILVERLIGHT && !__IOS__ && !__ANDROID__ && !NETSTANDARD

namespace NLog.Targets.Wrappers
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Runtime.InteropServices;
    using System.Security;
    using System.Security.Principal;
    using Common;
    using Internal;

    /// <summary>
    /// Impersonates another user for the duration of the write.
    /// </summary>
    /// <seealso href="https://github.com/nlog/nlog/wiki/ImpersonatingWrapper-target">Documentation on NLog Wiki</seealso>
    [SecuritySafeCritical]
    [Target("ImpersonatingWrapper", IsWrapper = true)]
    public class ImpersonatingTargetWrapper : WrapperTargetBase
    {
        private WindowsIdentity newIdentity;
        private IntPtr duplicateTokenHandle = IntPtr.Zero;

        /// <summary>
        /// Initializes a new instance of the <see cref="ImpersonatingTargetWrapper" /> class.
        /// </summary>
        public ImpersonatingTargetWrapper()
            : this(null)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ImpersonatingTargetWrapper" /> class.
        /// </summary>
        /// <param name="name">Name of the target.</param>
        /// <param name="wrappedTarget">The wrapped target.</param>
        public ImpersonatingTargetWrapper(string name, Target wrappedTarget)
            : this(wrappedTarget)
        {
            Name = name;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ImpersonatingTargetWrapper" /> class.
        /// </summary>
        /// <param name="wrappedTarget">The wrapped target.</param>
        public ImpersonatingTargetWrapper(Target wrappedTarget)
        {
            Domain = ".";
            LogOnType = SecurityLogOnType.Interactive;
            LogOnProvider = LogOnProviderType.Default;
            ImpersonationLevel = SecurityImpersonationLevel.Impersonation;
            WrappedTarget = wrappedTarget;
        }

        /// <summary>
        /// Gets or sets username to change context to.
        /// </summary>
        /// <docgen category='Impersonation Options' order='10' />
        public string UserName { get; set; }

        /// <summary>
        /// Gets or sets the user account password.
        /// </summary>
        /// <docgen category='Impersonation Options' order='10' />
        public string Password { get; set; }

        /// <summary>
        /// Gets or sets Windows domain name to change context to.
        /// </summary>
        /// <docgen category='Impersonation Options' order='10' />
        [DefaultValue(".")]
        public string Domain { get; set; }

        /// <summary>
        /// Gets or sets the Logon Type.
        /// </summary>
        /// <docgen category='Impersonation Options' order='10' />
        public SecurityLogOnType LogOnType { get; set; }

        /// <summary>
        /// Gets or sets the type of the logon provider.
        /// </summary>
        /// <docgen category='Impersonation Options' order='10' />
        public LogOnProviderType LogOnProvider { get; set; }

        /// <summary>
        /// Gets or sets the required impersonation level.
        /// </summary>
        /// <docgen category='Impersonation Options' order='10' />
        public SecurityImpersonationLevel ImpersonationLevel { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to revert to the credentials of the process instead of impersonating another user.
        /// </summary>
        /// <docgen category='Impersonation Options' order='10' />
        [DefaultValue(false)]
        public bool RevertToSelf { get; set; }

        /// <summary>
        /// Initializes the impersonation context.
        /// </summary>
        protected override void InitializeTarget()
        {
            if (!RevertToSelf)
            {
                newIdentity = CreateWindowsIdentity(out duplicateTokenHandle);
            }

            using (DoImpersonate())
            {
                base.InitializeTarget();
            }
        }

        /// <summary>
        /// Closes the impersonation context.
        /// </summary>
        protected override void CloseTarget()
        {
            using (DoImpersonate())
            {
                base.CloseTarget();
            }

            if (duplicateTokenHandle != IntPtr.Zero)
            {
                NativeMethods.CloseHandle(duplicateTokenHandle);
                duplicateTokenHandle = IntPtr.Zero;
            }

            if (newIdentity != null)
            {
                newIdentity.Dispose();
                newIdentity = null;
            }
        }

        /// <summary>
        /// Changes the security context, forwards the call to the <see cref="WrapperTargetBase.WrappedTarget"/>.Write()
        /// and switches the context back to original.
        /// </summary>
        /// <param name="logEvent">The log event.</param>
        protected override void Write(AsyncLogEventInfo logEvent)
        {
            using (DoImpersonate())
            {
                WrappedTarget.WriteAsyncLogEvent(logEvent);
            }
        }

        /// <summary>
        /// NOTE! Obsolete, instead override Write(IList{AsyncLogEventInfo} logEvents)
        /// 
        /// Writes an array of logging events to the log target. By default it iterates on all
        /// events and passes them to "Write" method. Inheriting classes can use this method to
        /// optimize batch writes.
        /// </summary>
        /// <param name="logEvents">Logging events to be written out.</param>
        [Obsolete("Instead override Write(IList<AsyncLogEventInfo> logEvents. Marked obsolete on NLog 4.5")]
        protected override void Write(AsyncLogEventInfo[] logEvents)
        {
            Write((IList<AsyncLogEventInfo>)logEvents);
        }

        /// <summary>
        /// Changes the security context, forwards the call to the <see cref="WrapperTargetBase.WrappedTarget"/>.Write()
        /// and switches the context back to original.
        /// </summary>
        /// <param name="logEvents">Log events.</param>
        protected override void Write(IList<AsyncLogEventInfo> logEvents)
        {
            using (DoImpersonate())
            {
                WrappedTarget.WriteAsyncLogEvents(logEvents);
            }
        }

        /// <summary>
        /// Flush any pending log messages (in case of asynchronous targets).
        /// </summary>
        /// <param name="asyncContinuation">The asynchronous continuation.</param>
        protected override void FlushAsync(AsyncContinuation asyncContinuation)
        {
            using (DoImpersonate())
            {
                WrappedTarget.Flush(asyncContinuation);
            }
        }

        private IDisposable DoImpersonate()
        {
            if (RevertToSelf)
            {
                return new ContextReverter(WindowsIdentity.Impersonate(IntPtr.Zero));
            }

            return new ContextReverter(newIdentity.Impersonate());
        }

        //
        // adapted from:
        // http://www.codeproject.com/csharp/cpimpersonation1.asp
        //
        private WindowsIdentity CreateWindowsIdentity(out IntPtr handle)
        {
            // initialize tokens
            IntPtr logonHandle;

            if (!NativeMethods.LogonUser(
                UserName,
                Domain,
                Password,
                (int)LogOnType,
                (int)LogOnProvider,
                out logonHandle))
            {
                throw Marshal.GetExceptionForHR(Marshal.GetHRForLastWin32Error());
            }

            if (!NativeMethods.DuplicateToken(logonHandle, (int)ImpersonationLevel, out handle))
            {
                NativeMethods.CloseHandle(logonHandle);
                throw Marshal.GetExceptionForHR(Marshal.GetHRForLastWin32Error());
            }

            NativeMethods.CloseHandle(logonHandle);

            // create new identity using new primary token)
            return new WindowsIdentity(handle);
        }

        /// <summary>
        /// Helper class which reverts the given <see cref="WindowsImpersonationContext"/> 
        /// to its original value as part of <see cref="IDisposable.Dispose"/>.
        /// </summary>
        internal class ContextReverter : IDisposable
        {
            private WindowsImpersonationContext wic;

            /// <summary>
            /// Initializes a new instance of the <see cref="ContextReverter" /> class.
            /// </summary>
            /// <param name="windowsImpersonationContext">The windows impersonation context.</param>
            public ContextReverter(WindowsImpersonationContext windowsImpersonationContext)
            {
                wic = windowsImpersonationContext;
            }

            /// <summary>
            /// Reverts the impersonation context.
            /// </summary>
            public void Dispose()
            {
                wic.Undo();
            }
        }
    }
}

#endif