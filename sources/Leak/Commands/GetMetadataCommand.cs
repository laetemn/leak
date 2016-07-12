﻿using Leak.Core.IO;
using Leak.Core.Net;
using Pargos;
using System;
using System.Linq;
using System.Threading;
using Leak.Core.Network;

namespace Leak.Commands
{
    public class GetMetadataCommand : Command
    {
        public override string Name
        {
            get { return "get-metadata"; }
        }

        public override void Execute(ArgumentCollection arguments)
        {
            foreach (GetMetadataTask task in GetMetadataTaskFactory.Find(arguments))
            {
                GetMetadata(task, arguments);
            }
        }

        private void GetMetadata(GetMetadataTask task, ArgumentCollection arguments)
        {
            MetainfoRepository repository = new MetainfoRepository(with =>
            {
                with.Directory(directory =>
                {
                    directory.Location = task.Output;
                    directory.Include("*.torrent");
                });
            });

            repository.Register(task.Hash);

            PeerNegotiator negotiator = new PeerNegotiatorEncrypted(with =>
            {
            });

            PeerClientFactory factory = new PeerClientFactory(with =>
            {
                with.Hash = task.Hash;
                with.Callback = new NegotiatorCallback(repository);
                with.Negotiator = negotiator;
                with.Options = PeerHandshakeOptions.Extended;
            });

            PeerListener listener = new PeerListener(with =>
            {
                with.Port = 8080;
                with.Callback = new NegotiatorCallback(repository);
                with.Negotiator = negotiator;
                with.Options = PeerHandshakeOptions.Extended;
                with.Hashes = new PeerNegotiatorHashCollection(task.Hash);
            });

            PeerAnnounce announce = new PeerAnnounce(with =>
            {
                with.Hash = task.Hash;
                with.Peer = task.Hash;

                if (arguments.Has("ip-address"))
                {
                    with.SetAddress(arguments.GetString("ip-address"));
                }
            });

            if (arguments.Has("enable-listening"))
            {
                listener.Listen();
            }

            foreach (MetainfoTracker tracker in task.Trackers.Where(TrackerClientFactory.IsSupported))
            {
                try
                {
                    TrackerClient client = TrackerClientFactory.Create(tracker);
                    TrackerResonse response = client.Announce(announce);

                    foreach (TrackerResponsePeer peer in response.Peers)
                    {
                        try
                        {
                            lock (typeof(Console))
                            {
                                factory.Connect(peer.Host, peer.Port);
                            }

                            Thread.Sleep(TimeSpan.FromSeconds(0.25));
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"{peer.Host}:{peer.Port} {ex.Message}");
                        }
                    }

                    Thread.Sleep(TimeSpan.FromSeconds(2));
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                }
            }

            factory.Connect("127.0.0.1", 8080);
            Thread.Sleep(TimeSpan.FromMinutes(5));
        }

        private class NegotiatorCallback : PeerNegotiatorCallback
        {
            private readonly MetainfoRepository repository;

            public NegotiatorCallback(MetainfoRepository repository)
            {
                this.repository = repository;
            }

            public void OnConnect(NetworkConnection connection)
            {
                Console.WriteLine($"Connected to {connection.Remote}.");
            }

            public void OnTerminate(NetworkConnection connection)
            {
            }

            public void OnHandshake(NetworkConnection connection, PeerHandshake handshake)
            {
                Console.WriteLine($"Handshake with {connection.Remote}.");

                if (handshake.Options.HasFlag(PeerHandshakeOptions.Extended))
                {
                    Console.WriteLine($"Accepted {connection.Remote}.");
                    handshake.Accept(new DownloadCallback(repository, handshake.Hash));
                }
            }
        }

        private class DownloadCallback : PeerCallback
        {
            private readonly byte[] hash;
            private readonly MetainfoRepository repository;

            private readonly PeerExtendedMapping mapping;
            private PeerExtendedMapping outgoing;
            private static int received = -1;

            public DownloadCallback(MetainfoRepository repository, byte[] hash)
            {
                this.hash = hash;
                this.repository = repository;

                this.mapping = new PeerExtendedMapping(with =>
                {
                    with.Extension("ut_metadata", 3);
                });
            }

            public override void OnAttached(PeerChannel channel)
            {
                channel.Send(new PeerExtended(with =>
                {
                    with.Handshake(mapping);
                }));
            }

            public override void OnTerminate(PeerChannel channel)
            {
            }

            public override void OnBitfield(PeerChannel channel, PeerBitfield message)
            {
            }

            public override void OnHave(PeerChannel channel, PeerHave message)
            {
            }

            public override void OnInterested(PeerChannel channel, PeerInterested message)
            {
            }

            public override void OnKeepAlive(PeerChannel channel)
            {
            }

            public override void OnPiece(PeerChannel channel, PeerPiece message)
            {
            }

            public override void OnUnchoke(PeerChannel channel, PeerUnchoke message)
            {
            }

            public override void OnExtended(PeerChannel channel, PeerExtended message)
            {
                Console.WriteLine($"Handling extended {message.Id} with {channel.Name}.");

                message.Handle(channel, with =>
                {
                    with.Mapping = mapping;
                    with.OnHandshake = OnHandshake;
                    with.OnMessage.Add("ut_metadata", OnMetadata);
                });
            }

            private void OnHandshake(PeerChannel channel, PeerExtendedMapping mapping)
            {
                outgoing = mapping;

                byte? id = outgoing.FindId("ut_metadata");
                int? size = outgoing.GetInt32("metadata_size");

                if (id != null && size != null)
                {
                    lock (repository)
                    {
                        if (repository.IsCompleted(hash) == false)
                        {
                            channel.Send(new PeerExtended(with =>
                            {
                                with.Id = id.Value;
                                with.MetadataRequest(request =>
                                {
                                    request.Piece = received + 1;
                                });
                            }));
                        }
                    }
                }
                else if (id == null)
                {
                    Console.WriteLine($"extended: no metadata support with {channel.Name}.");
                }
            }

            private void OnMetadata(PeerChannel channel, PeerExtended message)
            {
                Console.WriteLine($"Handling metadata {message.Id} with {channel.Name}.");

                message.HandleMetadata(channel, with =>
                {
                    with.OnRequest = OnMetadataRequest;
                    with.OnData = OnMetadataData;
                    with.OnReject = OnMetadataReject;
                });
            }

            private void OnMetadataRequest(PeerChannel channel, PeerExtendedMetadataRequest message)
            {
                Console.WriteLine($"Handling metadata request {message.Piece} with {channel.Name}.");

                channel.Send(new PeerExtended(with =>
                {
                    with.Id = outgoing.FindId("ut_metadata").Value;
                    with.MetadataReject(request =>
                    {
                        request.Piece = message.Piece;
                    });
                }));
            }

            private void OnMetadataData(PeerChannel channel, PeerExtendedMetadataData message)
            {
                Console.WriteLine($"Handling metadata piece {message.Piece} with {channel.Name}.");

                lock (repository)
                {
                    if (repository.IsCompleted(hash) == false)
                    {
                        if (repository.SetData(hash, message.Piece, message.Data) == false)
                        {
                            received = Math.Max(received, message.Piece);

                            channel.Send(new PeerExtended(with =>
                            {
                                with.Id = outgoing.FindId("ut_metadata").Value;
                                with.MetadataRequest(request =>
                                {
                                    request.Piece = received + 1;
                                });
                            }));
                        }
                    }
                }
            }

            private void OnMetadataReject(PeerChannel channel, PeerExtendedMetadataReject message)
            {
                Console.WriteLine($"Handling metadata reject {message.Piece} with {channel.Name}.");
            }
        }
    }
}