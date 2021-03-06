﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NServiceBus;
using NServiceBus.Unicast.Transport;
using System;
using System.Messaging;
using System.Transactions;
using NServiceBus.Utils;
namespace ReturnToSourceQueue.Controller
{
    class ErrorManager
    {
        private const string NonTransactionalQueueErrorMessageFormat = "Queue '{0}' must be transactional.";
        private const string NoMessageFoundErrorFormat = "INFO: No message found with ID '{0}'. Going to check headers of all messages for one with that original ID.";
        private MessageQueue queue;
        private static readonly TimeSpan TimeoutDuration = TimeSpan.FromSeconds(5);
        public bool ClusteredQueue { get; set; }
        /// <summary>
        /// Constant taken from V2.6: 
        /// https://github.com/NServiceBus/NServiceBus/blob/v2.5/src/impl/unicast/NServiceBus.Unicast.Msmq/MsmqTransport.cs
        /// </summary>
        private const string FAILEDQUEUE = "FailedQ";

        public virtual Address InputQueue
        {
            set
            {
                var path = MsmqUtilities.GetFullPath(value);
                var q = new MessageQueue(path);

                if ((!ClusteredQueue) && (!q.Transactional))
                    throw new ArgumentException(string.Format(NonTransactionalQueueErrorMessageFormat, q.Path));

                queue = q;

                var mpf = new MessagePropertyFilter();
                mpf.SetAll();

                queue.MessageReadPropertyFilter = mpf;
            }
        }

        public void ReturnAll()
        {
            for (int i = 0; i < 3000000; i++)
            {
                ReturnMessageToSourceQueue();
            }
            //foreach (var m in queue.GetAllMessages())
            //    ReturnMessageToSourceQueue(m.Id);
        }

        //public void ReturnMessageToSource(string id)
        //{
        //    using (TransactionScope scope = new TransactionScope())
        //    {
        //        Message m = queue.ReceiveById(id, TimeSpan.FromSeconds(5), MessageQueueTransactionType.Automatic);

        //        using (MessageQueue q = new MessageQueue(failedQueue))
        //        {
        //            q.Send(m, MessageQueueTransactionType.Automatic);
        //        }
        //        scope.Complete();
        //    }
        //}
        /// <summary>
        /// May throw a timeout exception if a message with the given id cannot be found.
        /// </summary>
        /// <param name="messageId"></param>
        public void ReturnMessageToSourceQueue()
        {
            using (var scope = new TransactionScope())
            {
                try
                {
                    var message = queue.Receive(MessageQueueTransactionType.Automatic);

                    var tm = MsmqUtilities.Convert(message);
                    string failedQ;
                    if (tm.Headers.ContainsKey(NServiceBus.Faults.FaultsHeaderKeys.FailedQ))
                        failedQ = tm.Headers[NServiceBus.Faults.FaultsHeaderKeys.FailedQ];
                    else // try to bring failedQ from label, v2.6 style.
                    {
                        failedQ = GetFailedQueueFromLabel(message);
                        if (!string.IsNullOrEmpty(failedQ))
                            message.Label = GetLabelWithoutFailedQueue(message);
                    }

                    if (string.IsNullOrEmpty(failedQ))
                    {
                        Console.WriteLine("ERROR: Message does not have a header (or label) indicating from which queue it came. Cannot be automatically returned to queue.");
                        return;
                    }

                    using (var q = new MessageQueue(MsmqUtilities.GetFullPath(Address.Parse(failedQ))))
                        q.Send(message, MessageQueueTransactionType.Automatic);

                    Console.WriteLine("Success.");
                    scope.Complete();
                }
                catch (MessageQueueException ex)
                {
                    Console.WriteLine("cannot send error: " + ex.Message);
                    //if (ex.MessageQueueErrorCode == MessageQueueErrorCode.IOTimeout)
                    //{
                    //    Console.WriteLine(NoMessageFoundErrorFormat, messageId);

                    //    foreach (var m in queue.GetAllMessages())
                    //    {
                    //        var tm = MsmqUtilities.Convert(m);

                    //        if (tm.Headers.ContainsKey(TransportHeaderKeys.OriginalId))
                    //        {
                    //            if (messageId != tm.Headers[TransportHeaderKeys.OriginalId])
                    //                continue;

                    //            Console.WriteLine("Found message - going to return to queue.");

                    //            using (var tx = new TransactionScope())
                    //            {
                    //                using (var q = new MessageQueue(
                    //                            MsmqUtilities.GetFullPath(
                    //                                Address.Parse(tm.Headers[NServiceBus.Faults.FaultsHeaderKeys.FailedQ]))))
                    //                    q.Send(m, MessageQueueTransactionType.Automatic);

                    //                queue.ReceiveByLookupId(MessageLookupAction.Current, m.LookupId,
                    //                                        MessageQueueTransactionType.Automatic);

                    //                tx.Complete();
                    //            }

                    //            Console.WriteLine("Success.");
                    //            scope.Complete();

                    //            return;
                    //        }
                    //    }
                    //}
                }
            }
        }

        /// <summary>
        /// For compatibility with V2.6:
        /// Gets the label of the message stripping out the failed queue.
        /// </summary>
        /// <param name="m"></param>
        /// <returns></returns>
        public static string GetLabelWithoutFailedQueue(Message m)
        {
            if (string.IsNullOrEmpty(m.Label))
                return string.Empty;

            if (!m.Label.Contains(FAILEDQUEUE))
                return m.Label;

            var startIndex = m.Label.IndexOf(string.Format("<{0}>", FAILEDQUEUE));
            var endIndex = m.Label.IndexOf(string.Format("</{0}>", FAILEDQUEUE));
            endIndex += FAILEDQUEUE.Length + 3;

            return m.Label.Remove(startIndex, endIndex - startIndex);
        }
        /// <summary>
        /// For compatibility with V2.6:
        /// Returns the queue whose process failed processing the given message
        /// by accessing the label of the message.
        /// </summary>
        /// <param name="m"></param>
        /// <returns></returns>
        public static string GetFailedQueueFromLabel(Message m)
        {
            if (m.Label == null)
                return null;

            if (!m.Label.Contains(FAILEDQUEUE))
                return null;

            var startIndex = m.Label.IndexOf(string.Format("<{0}>", FAILEDQUEUE)) + FAILEDQUEUE.Length + 2;
            var count = m.Label.IndexOf(string.Format("</{0}>", FAILEDQUEUE)) - startIndex;

            return m.Label.Substring(startIndex, count);
        }
    }
}
