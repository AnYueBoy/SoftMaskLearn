using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
#if !NET_STANDARD_2_0
using System.Reflection.Emit;
#endif
using UnityEngine;

namespace SoftMasking
{
    /// <summary>
    /// Mark an implementation of the IMaterialReplacer interface with this attribute to
    /// register it in the global material replacer chain. The global replacers will be
    /// used automatically by all SoftMasks.
    ///
    /// 用此属性标记 IMaterialReplacer 接口的实现，以将其注册到全局材质替换器链中。
    /// 此去全局替将被所有的SoftMask 自动使用。
    /// Globally registered replacers are called in order of ascending of their `order`
    /// value. The traversal is stopped on the first IMaterialReplacer which returns a
    /// non-null value and the returned value is the replacement being used.
    ///
    /// 全局注册的替换器会被以 它们的 'order'值 以升序的方式调用。遍历整个替换器 迭代器，
    /// 当检查到的替换器返回值不为空值时停止并应用这个返回的替换值。
    /// Implementation of IMaterialReplacer marked by this attribute should have a
    /// default constructor.
    ///
    /// 被这个属性标记的 IMaterialReplacer实现应该有一个默认构造。
    ///
    /// NOTE You may also need to mark global IMaterialReplacer by Unity's
    /// <see cref="UnityEngine.Scripting.PreserveAttribute"/> if you're using IL2CPP to
    /// prevent code stripping.
    /// </summary>
    /// <seealso cref="IMaterialReplacer"/>
    /// <seealso cref="MaterialReplacer.globalReplacers"/>
    /// 注意如果你使用IL2CPP,你可能还需要使用Unity的 PreserveAttribute 对全局 IMaterialReplacer
    /// 进行标记，以防止代码裁剪。
    [AttributeUsage(AttributeTargets.Class)]
    public class GlobalMaterialReplacerAttribute : Attribute
    {
    }

    /// <summary>
    /// Used by SoftMask to automatically replace materials which don't support
    /// Soft Mask by those that do.
    /// </summary>
    /// <seealso cref="GlobalMaterialReplacerAttribute"/>
    /// SoftMask 用于将不支持 Soft Mask 的材质自动替换为支持 Soft Mask 的材质。
    public interface IMaterialReplacer
    {
        /// <summary>
        /// Determines the mutual order in which IMaterialReplacers will be called.
        /// The lesser the return value, the earlier it will be called, that is,
        /// replacers are sorted by ascending of the `order` value.
        /// The order of default implementation is 0. If you want your function to be
        /// called before, return a value lesser than 0.
        /// </summary>
        /// 确定IMaterialReplacers调用的相互顺序。
        /// 返回值越小，调用的时间越早，也就是说，替换程序是按 "order "值升序排序的。
        /// 默认实现的顺序为0。如果你希望你的函数先被调用，请返回一个小于0的值。
        int order { get; }

        /// <summary>
        /// Should return null if this replacer can't replace the given material.
        /// </summary>
        /// 如果给定的材质无法替换将返回空
        Material Replace(Material material);
    }

    public static class MaterialReplacer
    {
        static List<IMaterialReplacer> _globalReplacers;

        /// <summary>
        /// Returns the collection of all globally registered replacers.
        /// </summary>
        /// <seealso cref="GlobalMaterialReplacerAttribute"/>
        public static IEnumerable<IMaterialReplacer> globalReplacers
        {
            get
            {
                if (_globalReplacers == null)
                    _globalReplacers = CollectGlobalReplacers().ToList();
                return _globalReplacers;
            }
        }

        static IEnumerable<IMaterialReplacer> CollectGlobalReplacers()
        {
            return AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(x => x.GetTypesSafe())
                .Where(IsMaterialReplacerType)
                .Select(TryCreateInstance)
                .Where(t => t != null);
        }

        static bool IsMaterialReplacerType(Type t)
        {
#if NET_STANDARD_2_0
            var isTypeBuilder = false;
#else
            var isTypeBuilder = t is TypeBuilder;
#endif
            return !isTypeBuilder
                   && !t.IsAbstract
                   && t.IsDefined(typeof(GlobalMaterialReplacerAttribute), false)
                   && typeof(IMaterialReplacer).IsAssignableFrom(t);
        }

        static IMaterialReplacer TryCreateInstance(Type t)
        {
            try
            {
                return (IMaterialReplacer)Activator.CreateInstance(t);
            }
            catch (Exception ex)
            {
                Debug.LogErrorFormat("Could not create instance of {0}: {1}", t.Name, ex);
                return null;
            }
        }

        static IEnumerable<Type> GetTypesSafe(this Assembly asm)
        {
            try
            {
                return asm.GetTypes();
            }
            catch (ReflectionTypeLoadException e)
            {
                return e.Types.Where(t => t != null);
            }
        }
    }

    public class MaterialReplacerChain : IMaterialReplacer
    {
        readonly List<IMaterialReplacer> _replacers;

        public MaterialReplacerChain(IEnumerable<IMaterialReplacer> replacers, IMaterialReplacer yetAnother)
        {
            _replacers = replacers.ToList();
            _replacers.Add(yetAnother);
            Initialize();
        }

        public int order { get; private set; }

        public Material Replace(Material material)
        {
            for (int i = 0; i < _replacers.Count; ++i)
            {
                var result = _replacers[i].Replace(material);
                if (result != null)
                    return result;
            }

            return null;
        }

        void Initialize()
        {
            _replacers.Sort((a, b) => a.order.CompareTo(b.order));
            order = _replacers[0].order;
        }
    }
}