using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using JetBrains.Annotations;

namespace Cybercore.Contracts
{
    public class Contract
    {
        [ContractAnnotation("predicate:false => halt")]
        public static void Requires<TException>(bool predicate, string message = null)
            where TException : Exception, new()
        {
            if (!predicate)
            {
                var constructor = constructors.GetOrAdd(typeof(TException), CreateConstructor);
                throw constructor(new object[] { message });
            }
        }

        [ContractAnnotation("parameter:null => halt")]
        public static void RequiresNonNull(object parameter, string paramName)
        {
            if (parameter == null)
                throw new ArgumentNullException(paramName);
        }

        #region Exception Constructors

        private static readonly ConcurrentDictionary<Type, ConstructorDelegate> constructors = new();

        private delegate Exception ConstructorDelegate(object[] parameters);

        private static ConstructorDelegate CreateConstructor(Type type)
        {
            var parameters = new[] { typeof(string) };
            var constructorInfo = type.GetTypeInfo().DeclaredConstructors.First(x => x.GetParameters().Length == 1 && x.GetParameters().First().ParameterType == typeof(string));
            var paramExpr = Expression.Parameter(typeof(object[]));

            var constructorParameters = parameters.Select((paramType, index) =>
                    Expression.Convert(
                        Expression.ArrayAccess(
                            paramExpr,
                            Expression.Constant(index)),
                        paramType)).ToArray();

            var body = Expression.New(constructorInfo, constructorParameters);

            var constructor = Expression.Lambda<ConstructorDelegate>(body, paramExpr);
            return constructor.Compile();
        }

        #endregion // Exception Constructors
    }
}