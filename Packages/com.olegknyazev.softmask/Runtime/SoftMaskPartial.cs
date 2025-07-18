using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.Sprites;
using SoftMasking.Extensions;

/// <summary>
/// Contains some predefined combinations of mask channel weights.
/// </summary>
public static class MaskChannel
{
    public static Color alpha = new Color(0, 0, 0, 1);
    public static Color red = new Color(1, 0, 0, 0);
    public static Color green = new Color(0, 1, 0, 0);
    public static Color blue = new Color(0, 0, 1, 0);
    public static Color gray = new Color(1, 1, 1, 0) / 3.0f;
}

namespace SoftMasking
{
    public partial class SoftMask
    {
        /// <summary>
        /// Source of the mask's image.
        /// </summary>
        [Serializable]
        public enum MaskSource
        {
            /// <summary>
            /// The mask image should be taken from the Graphic component of the containing
            /// GameObject. Only Image and RawImage components are supported. If there is no
            /// appropriate Graphic on the GameObject, a solid rectangle of the RectTransform
            /// dimensions will be used.
            /// </summary>
            /// Mask Image 从包含了GameObject的Graphic组件获取。只有Image和RawImage组件支持。如果GameObject上没有
            /// 合适的Graphic,将会使用RectTransform 固定矩形的尺寸。
            Graphic,

            /// <summary>
            /// The mask image should be taken from an explicitly specified Sprite. When this mode
            /// is used, spriteBorderMode can also be set to determine how to process Sprite's
            /// borders. If the sprite isn't set, a solid rectangle of the RectTransform dimensions
            /// will be used. This mode is analogous to using an Image with according sprite and
            /// type set.
            /// </summary>
            /// Mask Image 应取自于明确指定的Sprite。使用此模式时，还可以设置 spriteBorderMode 来决定如何处理
            /// Sprite 的边界。如果Sprite没有设置，将使用RectTransform尺寸的固定矩形。这种模式类似于
            /// 使用根据Sprite和类型设置的图像。
            Sprite,

            /// <summary>
            /// The mask image should be taken from an explicitly specified Texture2D or
            /// RenderTexture. When this mode is used, textureUVRect can also be set to determine
            /// which part of the texture should be used. If the texture isn't set, a solid rectangle
            /// of the RectTransform dimensions will be used. This mode is analogous to using a
            /// RawImage with according texture and uvRect set.
            /// </summary>
            /// Mask Image 应取自于明确指定的Texture2D 或者 RenderTexture。使用此模式时，可以设置textureUVRect
            /// 来决定texture的那个部分应该被使用。如果texture没有设置，将使用RectTransform尺寸的固定矩形。
            /// 该模式类似于使用根据纹理和 uvRect 设置的 RawImage。
            Texture
        }

        /// <summary>
        /// How Sprite's borders should be processed. It is a reduced set of Image.Type values.
        /// </summary>
        [Serializable]
        public enum BorderMode
        {
            /// <summary>
            /// Sprite should be drawn as a whole, ignoring any borders set. It works the
            /// same way as Unity's Image.Type.Simple.
            /// </summary>
            Simple,

            /// <summary>
            /// Sprite borders should be stretched when the drawn image is larger that the
            /// source. It works the same way as Unity's Image.Type.Sliced.
            /// </summary>
            Sliced,

            /// <summary>
            /// The same as Sliced, but border fragments will be repeated instead of
            /// stretched. It works the same way as Unity's Image.Type.Tiled.
            /// </summary>
            Tiled
        }

        /// <summary>
        /// Errors encountered during SoftMask diagnostics. Used by SoftMaskEditor to display
        /// hints relevant to the current state.
        /// </summary>
        [Flags]
        [Serializable]
        public enum Errors
        {
            NoError = 0,
            UnsupportedShaders = 1 << 0,
            NestedMasks = 1 << 1,
            TightPackedSprite = 1 << 2,
            AlphaSplitSprite = 1 << 3,
            UnsupportedImageType = 1 << 4,
            UnreadableTexture = 1 << 5,
            UnreadableRenderTexture = 1 << 6
        }

        struct Diagnostics
        {
            SoftMask _softMask;

            public Diagnostics(SoftMask softMask)
            {
                _softMask = softMask;
            }

            public Errors PollErrors()
            {
                var softMask = _softMask; // for use in lambda
                var result = Errors.NoError;
                softMask.GetComponentsInChildren(s_maskables);
                using (new ClearListAtExit<SoftMaskable>(s_maskables))
                    if (s_maskables.Any(m => ReferenceEquals(m.mask, softMask) && m.shaderIsNotSupported))
                        result |= Errors.UnsupportedShaders;
                if (ThereAreNestedMasks())
                    result |= Errors.NestedMasks;
                result |= CheckSprite(sprite);
                result |= CheckImage();
                result |= CheckTexture();
                return result;
            }

            public static Errors CheckSprite(Sprite sprite)
            {
                var result = Errors.NoError;
                if (!sprite) return result;
                if (sprite.packed && sprite.packingMode == SpritePackingMode.Tight)
                    result |= Errors.TightPackedSprite;
                if (sprite.associatedAlphaSplitTexture)
                    result |= Errors.AlphaSplitSprite;
                return result;
            }

            Image image => _softMask.DeduceSourceParameters().image;
            Sprite sprite => _softMask.DeduceSourceParameters().sprite;
            Texture texture => _softMask.DeduceSourceParameters().texture;

            bool ThereAreNestedMasks()
            {
                var softMask = _softMask; // for use in lambda
                var result = false;
                using (new ClearListAtExit<SoftMask>(s_masks))
                {
                    softMask.GetComponentsInParent(false, s_masks);
                    result |= s_masks.Any(x => AreCompeting(softMask, x));
                    softMask.GetComponentsInChildren(false, s_masks);
                    result |= s_masks.Any(x => AreCompeting(softMask, x));
                }

                return result;
            }

            Errors CheckImage()
            {
                var result = Errors.NoError;
                if (!_softMask.isBasedOnGraphic) return result;
                if (image && !IsImageTypeSupported(image.type))
                    result |= Errors.UnsupportedImageType;
                return result;
            }

            Errors CheckTexture()
            {
                var result = Errors.NoError;
                if (_softMask.isUsingRaycastFiltering && texture)
                {
                    var texture2D = texture as Texture2D;
                    if (!texture2D)
                        result |= Errors.UnreadableRenderTexture;
                    else if (!IsReadable(texture2D))
                        result |= Errors.UnreadableTexture;
                }

                return result;
            }

            static bool AreCompeting(SoftMask softMask, SoftMask other)
            {
                Assert.IsNotNull(other);
                return softMask.isMaskingEnabled
                       && softMask != other
                       && other.isMaskingEnabled
                       && softMask.canvas.rootCanvas == other.canvas.rootCanvas
                       && !SelectChild(softMask, other).canvas.overrideSorting;
            }

            static T SelectChild<T>(T first, T second) where T : Component
            {
                Assert.IsNotNull(first);
                Assert.IsNotNull(second);
                return first.transform.IsChildOf(second.transform) ? first : second;
            }

            static bool IsReadable(Texture2D texture)
            {
                try
                {
                    texture.GetPixel(0, 0);
                    return true;
                }
                catch (UnityException)
                {
                    return false;
                }
            }
        }

        struct WarningReporter
        {
            readonly UnityEngine.Object _owner;
            Texture _lastReadTexture;
            Sprite _lastUsedSprite;
            Sprite _lastUsedImageSprite;
            Image.Type _lastUsedImageType;

            public WarningReporter(UnityEngine.Object owner)
            {
                _owner = owner;
                _lastReadTexture = null;
                _lastUsedSprite = null;
                _lastUsedImageSprite = null;
                _lastUsedImageType = Image.Type.Simple;
            }

            public void TextureRead(Texture texture, MaterialParameters.SampleMaskResult sampleResult)
            {
                if (_lastReadTexture == texture)
                    return;
                _lastReadTexture = texture;
                switch (sampleResult)
                {
                    case MaterialParameters.SampleMaskResult.NonReadable:
                        Debug.LogErrorFormat(_owner,
                            "Raycast Threshold greater than 0 can't be used on Soft Mask with texture '{0}' because "
                            + "it's not readable. You can make the texture readable in the Texture Import Settings.",
                            texture.name);
                        break;
                    case MaterialParameters.SampleMaskResult.NonTexture2D:
                        Debug.LogErrorFormat(_owner,
                            "Raycast Threshold greater than 0 can't be used on Soft Mask with texture '{0}' because "
                            + "it's not a Texture2D. Raycast Threshold may be used only with regular 2D textures.",
                            texture.name);
                        break;
                }
            }

            public void SpriteUsed(Sprite sprite, Errors errors)
            {
                if (_lastUsedSprite == sprite)
                    return;
                _lastUsedSprite = sprite;
                if ((errors & Errors.TightPackedSprite) != 0)
                    Debug.LogError("SoftMask doesn't support tight packed sprites", _owner);
                if ((errors & Errors.AlphaSplitSprite) != 0)
                    Debug.LogError("SoftMask doesn't support sprites with an alpha split texture", _owner);
            }

            public void ImageUsed(Image image)
            {
                if (!image)
                {
                    _lastUsedImageSprite = null;
                    _lastUsedImageType = Image.Type.Simple;
                    return;
                }

                if (_lastUsedImageSprite == image.sprite && _lastUsedImageType == image.type)
                    return;
                _lastUsedImageSprite = image.sprite;
                _lastUsedImageType = image.type;
                if (!image)
                    return;
                if (IsImageTypeSupported(image.type))
                    return;
                Debug.LogErrorFormat(_owner,
                    "SoftMask doesn't support image type {0}. Image type Simple will be used.",
                    image.type);
            }
        }
    }
}