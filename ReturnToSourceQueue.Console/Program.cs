﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net;
using NServiceBus;
using System.Diagnostics;

namespace ReturnToSourceQueue.Controller
{
    class Program
    {
        static void Main(string[] args)
        {
            var errorManager = new ErrorManager();

            string inputQueue = null;
            string messageId = null;

            if (args != null && args.Length > 0)
                inputQueue = args[0];

            if (args != null && args.Length > 1)
                messageId = args[1];

            bool script = true;

            if (inputQueue == null)
            {
                Console.WriteLine("Please enter the error queue you would like to use:");
                inputQueue = Console.ReadLine();
                script = false;
            }

            Address errorQueueAddress = Address.Parse(inputQueue);

            if (!IsLocalIpAddress(errorQueueAddress.Machine))
            {
                Console.WriteLine(string.Format("Input queue [{0}] resides on a remote machine: [{1}].", errorQueueAddress.Queue, errorQueueAddress.Machine));
                Console.WriteLine("Due to networking load, it is advised to refrain from using ReturnToSourceQueue on a remote error queue, unless the error queue resides on a clustered machine.");
                if (!script)
                {
                    Console.WriteLine(
                        "Press 'y' if the error queue resides on a Clustered Machine, otherwise press any key to exit.");
                    if (Console.ReadKey().Key.ToString().ToLower() != "y")
                        Process.GetCurrentProcess().Kill();
                }
                Console.WriteLine(string.Empty);
                errorManager.ClusteredQueue = true;
            }

            if (messageId == null)
            {
                Console.WriteLine("Please enter the id of the message you'd like to return to its source queue, or 'all' to do so for all messages in the queue.");
                messageId = Console.ReadLine();
            }

            errorManager.InputQueue = errorQueueAddress;
            Console.WriteLine(string.Format("Attempting to return message to source queue. Queue: [{0}], message id: [{1}]. Please stand by.",
                errorQueueAddress.ToString(), messageId));

            try
            {
                if (messageId == "all")
                    errorManager.ReturnAll();
                else
                    errorManager.ReturnMessageToSourceQueue();

                if (args == null || args.Length == 0)
                {
                    Console.WriteLine("Press 'Enter' to exit.");
                    Console.ReadLine();
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("Could not return message to source queue. Reason: " + e.Message);
                Console.WriteLine(e.StackTrace);

                Console.WriteLine("\nPress 'Enter' to exit.");
                Console.ReadLine();
            }
        }
        public static bool IsLocalIpAddress(string host)
        {
            // get host IP addresses
            IPAddress[] hostIPs = Dns.GetHostAddresses(host);
            // get local IP addresses
            IPAddress[] localIPs = Dns.GetHostAddresses(Dns.GetHostName());

            // test if any host IP equals to any local IP or to localhost
            foreach (IPAddress hostIP in hostIPs)
            {
                // is localhost
                if (IPAddress.IsLoopback(hostIP)) return true;
                // is local address
                if (localIPs.Contains(hostIP)) return true;
            }
            return false;
        }
    }
}
