﻿#region copyright
/*
 * This code is from the Lawo/ember-plus GitHub repository and is licensed with
 *
 * Boost Software License - Version 1.0 - August 17th, 2003
 *
 * Permission is hereby granted, free of charge, to any person or organization
 * obtaining a copy of the software and accompanying documentation covered by
 * this license (the "Software") to use, reproduce, display, distribute,
 * execute, and transmit the Software, and to prepare derivative works of the
 * Software, and to permit third-parties to whom the Software is furnished to
 * do so, all subject to the following:
 *
 * The copyright notices in the Software and this entire statement, including
 * the above license grant, this restriction and the following disclaimer,
 * must be included in all copies of the Software, in whole or in part, and
 * all derivative works of the Software, unless such copies or derivative
 * works are solely in the form of machine-executable object code generated by
 * a source language processor.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE, TITLE AND NON-INFRINGEMENT. IN NO EVENT
 * SHALL THE COPYRIGHT HOLDERS OR ANYONE DISTRIBUTING THE SOFTWARE BE LIABLE
 * FOR ANY DAMAGES OR OTHER LIABILITY, WHETHER IN CONTRACT, TORT OR OTHERWISE,
 * ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
 * DEALINGS IN THE SOFTWARE.
 */
 #endregion

using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using System.Xml;
using EmberLib;
using EmberLib.Framing;
using EmberLib.Glow;
using EmberLib.Glow.Framing;
using EmberLib.Xml;
using EmberPlusProviderClassLib.Model;

namespace EmberPlusProviderClassLib
{
    public class Client : IDisposable
    {
        public Client(GlowListener host, Socket socket, int maxPackageLength, Dispatcher dispatcher)
        {
            Host = host;
            Socket = socket;
            MaxPackageLength = maxPackageLength;
            Dispatcher = dispatcher;

            _reader = new GlowReader(GlowReader_RootReady, GlowReader_KeepAliveRequestReceived);
            _reader.Error += GlowReader_Error;
            _reader.FramingError += GlowReader_FramingError;
        }

        public GlowListener Host { get; private set; }
        public Socket Socket { get; private set; }
        public int MaxPackageLength { get; }
        public Dispatcher Dispatcher { get; }

        public void Read(byte[] buffer, int count)
        {
            GlowReader reader;

            lock(_sync)
            {
                reader = _reader;
                //Console.WriteLine("Received {0} bytes from {1}", count, Socket.RemoteEndPoint);
            }

            reader?.ReadBytes(buffer, 0, count);
        }

        public void Write(GlowContainer glow)
        {
            var output = CreateOutput();

            glow.Encode(output);

            output.Finish();
        }

        public bool HasSubscribedToMatrix(Matrix matrix)
        {
            lock(_sync)
                return _subscribedMatrices.Contains(matrix);
        }

        public void SubscribeToMatrix(Matrix matrix, bool subscribe)
        {
            lock(_sync)
            {
                if(subscribe)
                {
                    if(_subscribedMatrices.Contains(matrix) == false)
                        _subscribedMatrices.AddLast(matrix);
                }
                else
                {
                    _subscribedMatrices.Remove(matrix);
                }
            }
        }

        #region Implementation
        GlowReader _reader;
        readonly LinkedList<Matrix> _subscribedMatrices = new LinkedList<Matrix>();
        readonly object _sync = new object();

        void GlowReader_RootReady(object sender, AsyncDomReader.RootReadyArgs e)
        {
            var root = e.Root as GlowContainer;

            if(root != null)
            {
                var buffer = new StringBuilder();
                var settings = new XmlWriterSettings
                {
                    OmitXmlDeclaration = true,
                    Indent = true,
                    IndentChars = "  ",
                };

                using(var writer = XmlWriter.Create(Console.Out, settings))
                    XmlExport.Export(root, writer);

                Dispatcher.DispatchGlow(root, this);
            }
            else
            {
                Console.WriteLine("Unexpected Ember Root: {0} ({1})", e.Root, e.Root.GetType());
            }
        }

        void GlowReader_Error(object sender, GlowReader.ErrorArgs e)
        {
            Console.WriteLine("GlowReader error {0}: {1}", e.ErrorCode, e.Message);
        }

        void GlowReader_FramingError(object sender, EmberLib.Framing.FramingReader.FramingErrorArgs e)
        {
            Console.WriteLine("GlowReader framing error: {0}", e.Message);
        }

        void GlowReader_KeepAliveRequestReceived(object sender, FramingReader.KeepAliveRequestReceivedArgs e)
        {
            Socket socket;

            lock(_sync)
                socket = Socket;

            socket?.Send(e.Response, e.ResponseLength, SocketFlags.None);
        }

        GlowOutput CreateOutput()
        {
            return new GlowOutput(MaxPackageLength, 0,
            (_, e) =>
            {
                Socket socket;
                GlowListener host;

                lock(_sync)
                {
                    socket = Socket;
                    host = Host;
                }

                if(socket != null)
                {
                    try
                    {
                        socket.Send(e.FramedPackage, e.FramedPackageLength, SocketFlags.None);
                    }
                    catch(SocketException)
                    {
                        host?.CloseClient(this);
                    }
                }
            });
        }
        #endregion

        #region IDisposable Members
        public void Dispose()
        {
            Socket socket;
            GlowReader reader;

            lock(_sync)
            {
                socket = Socket;
                reader = _reader;

                Socket = null;
                _reader = null;
                Host = null;
            }

            if(socket != null)
            {
                try
                {
                    socket.Close();
                }
                catch
                {
                }
            }

            reader?.Dispose();
        }
        #endregion
    }
}
