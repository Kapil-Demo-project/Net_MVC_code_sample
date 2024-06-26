using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Lib.SAJ.CoreStandard.MessageBus
{
    internal static class TypeHelper
    {
        internal const string InvalidTypeMessage = "Message must implement IMessageBusDocument";

        private static readonly ConcurrentDictionary<Type, bool> ImplementsICommandOfTResult;
        private static readonly ConcurrentDictionary<Type, Type?> CommandWithResultResultType;

        internal static readonly List<string> SupportedNamespaces;

        static TypeHelper()
        {
            SupportedNamespaces = new List<string>
            {
                "Lib.SAJ.CoreStandard",
                "Lib.SAJ.CoreStandard.UnitTests"
            };
            ImplementsICommandOfTResult = new();
            CommandWithResultResultType = new();
        }

        internal static string GetInvalidMessageTypeMessage()
        {
            var namespaces = SupportedNamespaces;

            string namespaceMessagePart = namespaces.Count == 1
                                              ? "the following namespace"
                                              : "one of the following namespaces";

            return string.Format(
                                 "Messages must belong to {0}:{1}{2}",
                                 namespaceMessagePart,
                                 Environment.NewLine,
                                 string.Concat(namespaces.Select(x => string.Format("\t{0}{1}", x, Environment.NewLine))));
        }

        /// <summary>
        /// The framework requires that messages should only be defined in a limited number of namespaces. 
        /// This method is used to enforce that.
        /// </summary>
        /// <param name="type">The type of the method in question.</param>
        /// <returns>Whether the message type is valid, based on its namespace.</returns>
        internal static bool IsValidMessageType(this Type type)
        {
            var typeName = type.FullName;

            // ReSharper disable once PossibleNullReferenceException
            return SupportedNamespaces.Any(typeName!.StartsWith);
        }

        private static bool IsFact(this Type typeToInspect)
        {
            return typeToInspect.ImplementsInterface(typeof(IFact));
        }

        internal static bool IsPriorityCommand(this Type typeToInspect)
        {
            return typeToInspect.ImplementsInterface(typeof(IPriorityCommand));
        }

        internal static bool IsMessageBusDocument(this Type typeToInspect)
        {
            return typeToInspect.ImplementsInterface(typeof(IMessageBusDocument));
        }

        private static bool IsCommand(this Type typeToInspect)
        {
            return typeToInspect.ImplementsInterface(typeof(ICommand));
        }

        private static bool IsCommandWithResult(this Type typeToInspect)
        {
            // Memoize this function for performance
            return ImplementsICommandOfTResult.GetOrAdd(
                typeToInspect,
                type =>
                    type.GetInterfaces()
                        .Any(x => x.GetTypeInfo().IsGenericType && x.GetGenericTypeDefinition() == typeof(ICommand<>)));
        }

        private static bool IsBroadcast(this Type typeToInspect)
        {
            return typeToInspect.ImplementsInterface(typeof(IBroadcast));
        }

        private static bool IsResult(this Type typeToInspect)
        {
            return typeToInspect.ImplementsInterface(typeof(IResult));
        }

        internal static Type GetCommandWithResultResultType(this Type typeToInspect)
        {
            // Memoize this function for performance
            var result = CommandWithResultResultType
                .GetOrAdd(typeToInspect,
                    t =>
                        t.GetInterfaces()
                            .Where(interfaceType => interfaceType.IsGenericType &&
                                                    interfaceType.GetGenericTypeDefinition() == typeof(ICommand<>))
                            .Select(interfaceType => interfaceType.GetGenericArguments()[0])
                            .FirstOrDefault());

            if (result == null)
            {
                throw new NotSupportedException("Must implement ICommand<TResult> for any ICommandWithResult");
            }

            return result;
        }

        internal static bool IsUnreliableConnectionOk(this Type typeToInspect)
        {
            return typeToInspect.ImplementsInterface(typeof(IUnreliableConnectIsOk));
        }

        internal static bool IsTemporary(this Type typeToInspect)
        {
            return typeToInspect.ImplementsInterface(typeof(IMessageBusTemporary));
        }

        internal static TimeSpan? GetMessageTtl(this Type typeToInspect)
        {
            return typeToInspect.GetCustomAttribute<MessageTtlAttribute>()?.Ttl;
        }

        internal static MessageType GetMessageType(this Type typeToInspect)
        {
            if (typeToInspect.IsFact())
            {
                return MessageType.Fact;
            }

            if (typeToInspect.IsCommand())
            {
                return MessageType.Command;
            }

            if (typeToInspect.IsCommandWithResult())
            {
                return MessageType.CommandWithResult;
            }

            if (typeToInspect.IsResult())
            {
                return MessageType.Result;
            }

            if (typeToInspect.IsBroadcast())
            {
                return MessageType.Broadcast;
            }

            throw new MessageBusException($"Unknown MessageBus document type for {typeToInspect}");
        }

        private static bool ImplementsInterface(this Type typeToInspect, Type interfaceType)
        {
            return interfaceType.IsAssignableFrom(typeToInspect);
        }
    }
}
