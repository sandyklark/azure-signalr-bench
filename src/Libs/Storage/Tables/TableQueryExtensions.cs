﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Linq.Expressions;
using System.Reflection;

using Microsoft.Azure.Cosmos.Table;

namespace Azure.SignalRBench.Storage
{
    public static class TableQueryExtensions
    {
        public static TableQuery<T> Where<T>(this TableQuery<T> query, Expression<Func<T, bool>> predicate)
        {
            if (!(predicate.Body is BinaryExpression bin))
            {
                throw new NotSupportedException($"Expected binary expression, actual {predicate.Body.NodeType}.");
            }
            var filter = GetFilter(bin);
            if (string.IsNullOrEmpty(query.FilterString))
            {
                return query.Where(filter);
            }
            return query.Where(TableQuery.CombineFilters(query.FilterString, TableOperators.And, filter));
        }

        private static string GetFilter(Expression expression)
        {
            switch (expression.NodeType)
            {
                case ExpressionType.Not:
                    var unary = (UnaryExpression)expression;
                    return TableQuery.CombineFilters(string.Empty, TableOperators.Not, GetFilter(unary.Operand));
                case ExpressionType.And:
                case ExpressionType.AndAlso:
                    var bin = (BinaryExpression)expression;
                    return TableQuery.CombineFilters(GetFilter(bin.Left), TableOperators.And, GetFilter(bin.Right));
                case ExpressionType.Or:
                case ExpressionType.OrElse:
                    bin = (BinaryExpression)expression;
                    return TableQuery.CombineFilters(GetFilter(bin.Left), TableOperators.Or, GetFilter(bin.Right));
                case ExpressionType.Equal:
                    bin = (BinaryExpression)expression;
                    return GenerateFilterCondition(GetProperty(bin.Left), QueryComparisons.Equal, bin.Right);
                case ExpressionType.NotEqual:
                    bin = (BinaryExpression)expression;
                    return GenerateFilterCondition(GetProperty(bin.Left), QueryComparisons.NotEqual, bin.Right);
                case ExpressionType.GreaterThan:
                    bin = (BinaryExpression)expression;
                    return GenerateFilterCondition(GetProperty(bin.Left), QueryComparisons.GreaterThan, bin.Right);
                case ExpressionType.GreaterThanOrEqual:
                    bin = (BinaryExpression)expression;
                    return GenerateFilterCondition(GetProperty(bin.Left), QueryComparisons.GreaterThanOrEqual, bin.Right);
                case ExpressionType.LessThan:
                    bin = (BinaryExpression)expression;
                    return GenerateFilterCondition(GetProperty(bin.Left), QueryComparisons.LessThan, bin.Right);
                case ExpressionType.LessThanOrEqual:
                    bin = (BinaryExpression)expression;
                    return GenerateFilterCondition(GetProperty(bin.Left), QueryComparisons.LessThanOrEqual, bin.Right);
                case ExpressionType.IsFalse:
                    unary = (UnaryExpression)expression;
                    return GenerateFilterCondition(GetProperty(unary.Operand), QueryComparisons.Equal, false);
                case ExpressionType.IsTrue:
                    unary = (UnaryExpression)expression;
                    return GenerateFilterCondition(GetProperty(unary.Operand), QueryComparisons.Equal, true);
                case ExpressionType.Coalesce:
                default:
                    throw new InvalidOperationException($"Expected binary expression, actual {expression.NodeType}.");
            }
        }

        private static (string name, Type type) GetProperty(Expression expression)
        {
            if (expression is MemberExpression me)
            {
                if (me.Member is PropertyInfo pi)
                {
                    return (pi.Name, pi.PropertyType);
                }
            }
            throw new InvalidOperationException($"Expected property expression, actual {expression.NodeType}.");
        }

        private static string GenerateFilterCondition((string name, Type type) nameAndType, string comparison, Expression valueExpression)
        {
            var (name, type) = nameAndType;
            if (type == typeof(string))
            {
                return TableQuery.GenerateFilterCondition(name, comparison, GetValue<string>(valueExpression));
            }
            if (type == typeof(bool) || type == typeof(bool?))
            {
                return TableQuery.GenerateFilterConditionForBool(name, comparison, GetValue<bool>(valueExpression));
            }
            if (type == typeof(DateTime) || type == typeof(DateTime?))
            {
                return TableQuery.GenerateFilterConditionForDate(name, comparison, GetValue<DateTime>(valueExpression));
            }
            if (type == typeof(DateTimeOffset) || type == typeof(DateTimeOffset?))
            {
                return TableQuery.GenerateFilterConditionForDate(name, comparison, GetValue<DateTimeOffset>(valueExpression));
            }
            if (type == typeof(double) || type == typeof(double?))
            {
                return TableQuery.GenerateFilterConditionForDouble(name, comparison, GetValue<double>(valueExpression));
            }
            if (type == typeof(int) || type == typeof(int?))
            {
                return TableQuery.GenerateFilterConditionForInt(name, comparison, GetValue<int>(valueExpression));
            }
            if (type == typeof(long) || type == typeof(long?))
            {
                return TableQuery.GenerateFilterConditionForLong(name, comparison, GetValue<long>(valueExpression));
            }
            throw new InvalidOperationException($"Type {type} is not supported.");
        }

        private static string GenerateFilterCondition((string name, Type type) nameAndType, string comparison, bool value)
        {
            return TableQuery.GenerateFilterConditionForBool(nameAndType.name, comparison, value);
        }

        private static T GetValue<T>(Expression expression)
        {
            switch (expression.NodeType)
            {
                case ExpressionType.Convert:
                    var unary = (UnaryExpression)expression;
                    if (unary.Type == typeof(ColumnValue))
                    {
                        var cv = Expression.Lambda<Func<ColumnValue>>(expression).Compile(true).Invoke();
                        return (T)cv.Value;
                    }
                    goto case ExpressionType.MemberAccess;
                case ExpressionType.MemberAccess:
                    var result = Expression.Lambda<Func<T>>(expression).Compile(true).Invoke();
                    return result;
                case ExpressionType.Constant:
                    return (T)((ConstantExpression)expression).Value;
                default:
                    throw new InvalidOperationException($"Expected parameter or constrant, actual {expression.NodeType}.");
            }
        }
    }
}
