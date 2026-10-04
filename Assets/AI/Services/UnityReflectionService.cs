using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace AI.Services
{
    public class UnityReflectionService
    {
        private readonly Dictionary<string, Type> _typeCache =
            new Dictionary<string, Type>();

        private readonly Dictionary<Type, MethodInfo[]> _methodCache =
            new Dictionary<Type, MethodInfo[]>();

        private readonly Dictionary<Type, PropertyInfo[]> _propertyCache =
            new Dictionary<Type, PropertyInfo[]>();

        private readonly Dictionary<Type, FieldInfo[]> _fieldCache =
            new Dictionary<Type, FieldInfo[]>();

        public UnityReflectionService()
        {
            CacheUnityTypes();
        }

        //----------------------------------------------------------

        void CacheUnityTypes()
        {
            _typeCache.Clear();

            var assemblies = AppDomain.CurrentDomain.GetAssemblies();

            foreach (var asm in assemblies)
            {
                Type[] types;

                try
                {
                    types = asm.GetTypes();
                }
                catch
                {
                    continue;
                }

                foreach (var type in types)
                {
                    if (type == null)
                        continue;

                    if (_typeCache.ContainsKey(type.Name))
                        continue;

                    _typeCache.Add(type.Name, type);
                }
            }
        }

        //----------------------------------------------------------

        public bool HasType(string name)
        {
            return _typeCache.ContainsKey(name);
        }

        //----------------------------------------------------------

        public Type GetType(string name)
        {
            _typeCache.TryGetValue(name, out var type);
            return type;
        }

        //----------------------------------------------------------

        public List<string> GetAllTypes()
        {
            return _typeCache.Keys
                .OrderBy(x => x)
                .ToList();
        }

        //----------------------------------------------------------

        public MethodInfo[] GetMethods(Type type)
        {
            if (type == null)
                return Array.Empty<MethodInfo>();

            if (_methodCache.TryGetValue(type, out var methods))
                return methods;

            methods = type.GetMethods(
                BindingFlags.Public |
                BindingFlags.Instance |
                BindingFlags.Static);

            _methodCache[type] = methods;

            return methods;
        }

        //----------------------------------------------------------

        public PropertyInfo[] GetProperties(Type type)
        {
            if (type == null)
                return Array.Empty<PropertyInfo>();

            if (_propertyCache.TryGetValue(type, out var props))
                return props;

            props = type.GetProperties(
                BindingFlags.Public |
                BindingFlags.Instance |
                BindingFlags.Static);

            _propertyCache[type] = props;

            return props;
        }

        //----------------------------------------------------------

        public FieldInfo[] GetFields(Type type)
        {
            if (type == null)
                return Array.Empty<FieldInfo>();

            if (_fieldCache.TryGetValue(type, out var fields))
                return fields;

            fields = type.GetFields(
                BindingFlags.Public |
                BindingFlags.Instance |
                BindingFlags.Static);

            _fieldCache[type] = fields;

            return fields;
        }

        //----------------------------------------------------------

        public List<Type> GetComponents()
        {
            return _typeCache.Values
                .Where(t =>
                    t.IsSubclassOf(typeof(Component)) &&
                    !t.IsAbstract)
                .OrderBy(t => t.Name)
                .ToList();
        }

        //----------------------------------------------------------

        public List<Type> GetScriptableObjects()
        {
            return _typeCache.Values
                .Where(t =>
                    t.IsSubclassOf(typeof(ScriptableObject)) &&
                    !t.IsAbstract)
                .OrderBy(t => t.Name)
                .ToList();
        }

        //----------------------------------------------------------

        public bool HasMethod(Type type, string methodName)
        {
            return GetMethods(type)
                .Any(m => m.Name == methodName);
        }

        //----------------------------------------------------------

        public MethodInfo GetMethod(Type type, string methodName)
        {
            return GetMethods(type)
                .FirstOrDefault(m => m.Name == methodName);
        }

        //----------------------------------------------------------

        public List<string> GetMethodNames(Type type)
        {
            return GetMethods(type)
                .Select(m => m.Name)
                .Distinct()
                .OrderBy(n => n)
                .ToList();
        }

        //----------------------------------------------------------

        public List<string> GetPropertyNames(Type type)
        {
            return GetProperties(type)
                .Select(p => p.Name)
                .Distinct()
                .OrderBy(n => n)
                .ToList();
        }

        //----------------------------------------------------------

        public List<string> GetFieldNames(Type type)
        {
            return GetFields(type)
                .Select(f => f.Name)
                .Distinct()
                .OrderBy(n => n)
                .ToList();
        }

        //----------------------------------------------------------

        public int TotalTypes =>
            _typeCache.Count;

        public int TotalComponents =>
            GetComponents().Count;

        public int TotalScriptableObjects =>
            GetScriptableObjects().Count;
    }
}