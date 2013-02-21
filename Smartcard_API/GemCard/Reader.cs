﻿/**
 * @author Olivier ROUIT
 * 
 * @license CPL, CodeProject license 
 */

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace Core.Smartcard
{
    public class Reader : IDisposable
    {
        private string readerName;
        private const uint INFINITE = 0xFFFFFFFF;
        private const uint WAIT_TIME = 250;
        private bool runCardDetectionFlag = true;
        private Thread cardDetectThread = null;

        /// <summary>
        /// Event handler for the card insertion
        /// </summary>
        public event CardInsertedEventHandler CardInserted = null;

        /// <summary>
        /// Event handler for the card removal
        /// </summary>
        public event CardRemovedEventHandler CardRemoved = null;

        public Reader(string readerName)
        {
            this.readerName = readerName;
        }

        /// <summary>
        /// This function must implement a card detection mechanism.
        /// 
        /// When card insertion is detected, it must call the method CardInserted()
        /// When card removal is detected, it must call the method CardRemoved()
        /// 
        /// </summary>
        protected void RunCardDetection(object Reader)
        {
            bool bFirstLoop = true;
            IntPtr hContext = IntPtr.Zero;    // Local context
            IntPtr phContext;

            phContext = Marshal.AllocHGlobal(Marshal.SizeOf(hContext));

            if (PCSC.SCardEstablishContext((uint)SCOPE.User, IntPtr.Zero, IntPtr.Zero, phContext) == 0)
            {
                hContext = Marshal.ReadIntPtr(phContext);
                Marshal.FreeHGlobal(phContext);

                UInt32 nbReaders = 1;
                PCSC.SCard_ReaderState[] readerState = new PCSC.SCard_ReaderState[nbReaders];

                readerState[0].m_dwCurrentState = (UInt32)CARD_STATE.UNAWARE;
                readerState[0].m_szReader = (string)Reader;

                UInt32 eventState;
                UInt32 currentState = readerState[0].m_dwCurrentState;

                // Card detection loop
                do
                {
                    if (PCSC.SCardGetStatusChange(hContext, WAIT_TIME, readerState, nbReaders) == 0)
                    {
                        eventState = readerState[0].m_dwEventState;
                        currentState = readerState[0].m_dwCurrentState;

                        // Check state
                        if (((eventState & (uint)CARD_STATE.CHANGED) == (uint)CARD_STATE.CHANGED) && !bFirstLoop)
                        {
                            // State has changed
                            if ((eventState & (uint)CARD_STATE.EMPTY) == (uint)CARD_STATE.EMPTY)
                            {
                                // There is no card, card has been removed -> Fire CardRemoved event
                                RaiseCardRemoved((string)Reader);
                            }

                            if (((eventState & (uint)CARD_STATE.PRESENT) == (uint)CARD_STATE.PRESENT) &&
                                ((eventState & (uint)CARD_STATE.PRESENT) != (currentState & (uint)CARD_STATE.PRESENT)))
                            {
                                // There is a card in the reader -> Fire CardInserted event
                                RaiseCardInserted((string)Reader);
                            }

                            if ((eventState & (uint)CARD_STATE.ATRMATCH) == (uint)CARD_STATE.ATRMATCH)
                            {
                                // There is a card in the reader and it matches the ATR we were expecting-> Fire CardInserted event
                                RaiseCardInserted((string)Reader);
                            }
                        }

                        // The current stateis now the event state
                        readerState[0].m_dwCurrentState = eventState;

                        bFirstLoop = false;
                    }

                    Thread.Sleep(100);

                    if (runCardDetectionFlag == false)
                        break;
                }
                while (true);    // Exit on request
            }
            else
            {
                Marshal.FreeHGlobal(phContext);
                throw new Exception("PC/SC error");
            }

            PCSC.SCardReleaseContext(hContext);
        }

        private void RaiseCardRemoved(string reader)
        {
            if (CardRemoved != null)
            {
                CardRemoved(this, reader);
            }
        }

        private void RaiseCardInserted(string reader)
        {
            if (CardInserted != null)
            {
                CardInserted(this, reader);
            }
        }

        public void Dispose()
        {
            throw new NotImplementedException();
        }

        #region Static methods

        /// <summary>
        /// Wraps the PCSC function 
        /// LONG SCardEstablishContext(
        ///		IN DWORD dwScope,
        ///		IN LPCVOID pvReserved1,
        ///		IN LPCVOID pvReserved2,
        ///		OUT LPSCARDCONTEXT phContext
        ///	);
        /// </summary>
        /// <param name="Scope"></param>
        private static IntPtr EstablishContext(SCOPE Scope)
        {
            int lastError = PCSC.SCARD_S_SUCCESS;
            IntPtr hContextRet = IntPtr.Zero;
            IntPtr hContext = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(IntPtr)));

            lastError = PCSC.SCardEstablishContext((uint)Scope, IntPtr.Zero, IntPtr.Zero, hContext);
            if (lastError != 0)
            {
                Marshal.FreeHGlobal(hContext);
                ThrowSmartcardException("SCardEstablishContext", lastError);
            }

            hContextRet = Marshal.ReadIntPtr(hContext);
            Marshal.FreeHGlobal(hContext);

            return hContextRet;
        }

        /// <summary>
        /// Wraps the PCSC function
        /// LONG SCardReleaseContext(
        ///		IN SCARDCONTEXT hContext
        ///	);
        /// </summary>
        private static void ReleaseContext(IntPtr hContext)
        {
            if (PCSC.SCardIsValidContext(hContext) == PCSC.SCARD_S_SUCCESS)
            {
                int lastError = PCSC.SCardReleaseContext(hContext);
                ThrowSmartcardException("SCardReleaseContext", lastError);

                //Marshal.FreeHGlobal(hContext);
                hContext = IntPtr.Zero;
            }
        }

        /// <summary>
        /// Wraps the PCSC function
        /// LONG SCardListReaders(SCARDCONTEXT hContext, 
        ///		LPCTSTR mszGroups, 
        ///		LPTSTR mszReaders, 
        ///		LPDWORD pcchReaders 
        ///	);
        /// </summary>
        /// <returns>A string array of the readers</returns>
        public static string[] ListReaders()
        {
            IntPtr hContext = EstablishContext(SCOPE.User);

            string[] sListReaders = null;
            UInt32 pchReaders = 0;
            IntPtr szListReaders = IntPtr.Zero;

            int lastError = PCSC.SCardListReaders(hContext, null, szListReaders, out pchReaders);
            if (lastError == 0)
            {
                szListReaders = Marshal.AllocHGlobal((int)pchReaders);
                lastError = PCSC.SCardListReaders(hContext, null, szListReaders, out pchReaders);
                if (lastError == 0)
                {
                    char[] caReadersData = new char[pchReaders];
                    int nbReaders = 0;
                    for (int nI = 0; nI < pchReaders; nI++)
                    {
                        caReadersData[nI] = (char)Marshal.ReadByte(szListReaders, nI);

                        if (caReadersData[nI] == 0)
                            nbReaders++;
                    }

                    // Remove last 0
                    --nbReaders;

                    if (nbReaders != 0)
                    {
                        sListReaders = new string[nbReaders];
                        char[] caReader = new char[pchReaders];
                        int nIdx = 0;
                        int nIdy = 0;
                        int nIdz = 0;
                        // Get the nJ string from the multi-string

                        while (nIdx < pchReaders - 1)
                        {
                            caReader[nIdy] = caReadersData[nIdx];
                            if (caReader[nIdy] == 0)
                            {
                                sListReaders[nIdz] = new string(caReader, 0, nIdy);
                                ++nIdz;
                                nIdy = 0;
                                caReader = new char[pchReaders];
                            }
                            else
                                ++nIdy;

                            ++nIdx;
                        }
                    }
                }

                Marshal.FreeHGlobal(szListReaders);
            }

            ReleaseContext(hContext);

            ThrowSmartcardException("SCardListReaders", lastError);

            return sListReaders;
        }

        private static void ThrowSmartcardException(string methodName, long errCode)
        {
            if (errCode != 0)
            {
                throw new SmartCardException(string.Format("{0} error: {1:X02}", methodName, errCode));
            }
        }

        #endregion
    }
}