﻿using DbgEngManaged;
using System;

namespace CsDebugScript.Engine.Debuggers.DbgEngDllHelpers
{
    /// <summary>
    /// Controler for Debugee actions during live debugging.
    /// </summary>
    class DebuggeeFlowController
    {
        /// <summary>
        /// Signal fired during interactive process debugging when debugee is released.
        /// </summary>
        public System.Threading.AutoResetEvent DebugStatusGo { get; private set; }

        /// <summary>
        /// Signal fired during interactive process debugging when debugee is interrupted.
        /// </summary>
        public System.Threading.AutoResetEvent DebugStatusBreak { get; private set; }

        /// <summary>
        /// Loop responsible for catching debug events and signaling debugee state.
        /// </summary>
        private System.Threading.Thread debuggerStateLoop;

        /// <summary>
        /// Syncronization signaling that debug callbacks are installed.
        /// </summary>
        private static readonly object eventCallbacksReady = new Object();

        /// <summary>
        /// Initializes a new instance of the <see cref="DebuggeeFlowController"/> class.
        /// </summary>
        /// <param name="client">The client.</param>
        public DebuggeeFlowController(IDebugClient client)
        {
            DebugStatusGo = new System.Threading.AutoResetEvent(false);

            // Default is that we start in break mode.
            // TODO: Needs to be changed when we allow non intrusive attach/start for example.
            //
            DebugStatusBreak = new System.Threading.AutoResetEvent(true);

            lock (eventCallbacksReady)
            {
                debuggerStateLoop =
                    new System.Threading.Thread(() => DebuggerStateLoop(client, DebugStatusGo, DebugStatusBreak)) { IsBackground = true };
                debuggerStateLoop.SetApartmentState(System.Threading.ApartmentState.MTA);
                debuggerStateLoop.Start();

                // Wait for loop thread to become ready.
                //
                System.Threading.Monitor.Wait(eventCallbacksReady);
            }
        }

        /// <summary>
        /// Loop responsible to wait for debug events.
        /// Needs to be run in separate thread.
        /// </summary>
        private static void DebuggerStateLoop(
            IDebugClient client,
            System.Threading.AutoResetEvent debugStatusGo,
            System.Threading.AutoResetEvent debugStatusBreak)
        {
            bool hasClientExited = false;
            IDebugControl7 loopControl = (IDebugControl7)client;
            DebugCallbacks eventCallbacks = new DebugCallbacks(client, debugStatusGo);

            lock (eventCallbacksReady)
            {
                System.Threading.Monitor.Pulse(eventCallbacksReady);
            }

            // Default is to start in break mode, wait for release.
            //
            debugStatusGo.WaitOne();

            while (!hasClientExited)
            {
                loopControl.WaitForEvent(0, UInt32.MaxValue);
                uint executionStatus = loopControl.GetExecutionStatus();

                while (executionStatus == (uint)Defines.DebugStatusBreak)
                {
                    debugStatusBreak.Set();
                    debugStatusGo.WaitOne();

                    executionStatus = loopControl.GetExecutionStatus();
                }

                hasClientExited = executionStatus == (uint)Defines.DebugStatusNoDebuggee;
            }
        }

        /// <summary>
        /// Waits for debugger loop thread to finish.
        /// </summary>
        public void WaitForDebuggerLoopToExit()
        {
            debuggerStateLoop.Join();
        }
    }
}
