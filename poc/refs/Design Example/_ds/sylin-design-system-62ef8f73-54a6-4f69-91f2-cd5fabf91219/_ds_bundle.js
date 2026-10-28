/* @ds-bundle: {"format":4,"namespace":"SylinDesignSystem_62ef8f","components":[{"name":"Button","sourcePath":"components/core/Button.jsx"},{"name":"Card","sourcePath":"components/core/Card.jsx"},{"name":"ExternalLink","sourcePath":"components/core/ExternalLink.jsx"},{"name":"Icon","sourcePath":"components/core/Icon.jsx"},{"name":"ICON_NAMES","sourcePath":"components/core/Icon.jsx"},{"name":"PixelSprite","sourcePath":"components/core/PixelSprite.jsx"},{"name":"Tag","sourcePath":"components/core/Tag.jsx"},{"name":"Callout","sourcePath":"components/patterns/Callout.jsx"},{"name":"CommitmentCard","sourcePath":"components/patterns/CommitmentCard.jsx"},{"name":"MaturityLabel","sourcePath":"components/patterns/MaturityLabel.jsx"},{"name":"ProjectCard","sourcePath":"components/patterns/ProjectCard.jsx"},{"name":"Stat","sourcePath":"components/patterns/Stat.jsx"},{"name":"StatRow","sourcePath":"components/patterns/Stat.jsx"}],"sourceHashes":{"components/core/Button.jsx":"de862c8c7a29","components/core/Card.jsx":"2d0d33dd74d2","components/core/ExternalLink.jsx":"21848a6be738","components/core/Icon.jsx":"df0d0c094f64","components/core/PixelSprite.jsx":"89c5e52e1cf2","components/core/Tag.jsx":"4085a0a649ce","components/patterns/Callout.jsx":"13f2a585d575","components/patterns/CommitmentCard.jsx":"b3f0f5b7352b","components/patterns/MaturityLabel.jsx":"aa11c05fa0d6","components/patterns/ProjectCard.jsx":"11261319d2a1","components/patterns/Stat.jsx":"568ff8649c18"},"inlinedExternals":[],"unexposedExports":[]} */

(() => {

const __ds_ns = (window.SylinDesignSystem_62ef8f = window.SylinDesignSystem_62ef8f || {});

const __ds_scope = {};

(__ds_ns.__errors = __ds_ns.__errors || []);

// components/core/Card.jsx
try { (() => {
function _extends() { return _extends = Object.assign ? Object.assign.bind() : function (n) { for (var e = 1; e < arguments.length; e++) { var t = arguments[e]; for (var r in t) ({}).hasOwnProperty.call(t, r) && (n[r] = t[r]); } return n; }, _extends.apply(null, arguments); }
/**
 * Card — the base surface. A quiet panel on the dusk ground. Flat by
 * default (border + faint fill); `raised` adds a soft shadow; `glow`
 * lights the border with the lantern flame for the one thing worth
 * drawing the eye to.
 */
function Card({
  children,
  variant = "flat",
  padding = "lg",
  as = "div",
  style,
  ...rest
}) {
  const pads = {
    none: "0",
    sm: "var(--space-4)",
    md: "var(--space-5)",
    lg: "var(--space-6)"
  }[padding];
  const variants = {
    flat: {
      background: "var(--surface-card)",
      border: "1px solid var(--border-line)"
    },
    sunken: {
      background: "var(--surface-sunken)",
      border: "1px solid var(--border-hairline)",
      boxShadow: "var(--shadow-inset)"
    },
    raised: {
      background: "var(--surface-raised)",
      border: "1px solid var(--border-line)",
      boxShadow: "var(--shadow-md)"
    },
    glow: {
      background: "var(--surface-card)",
      border: "1px solid var(--accent-line)",
      boxShadow: "var(--glow-flame-sm)"
    }
  }[variant];
  const Tag = as;
  return /*#__PURE__*/React.createElement(Tag, _extends({
    className: `syl-card syl-card--${variant}`,
    style: {
      borderRadius: "var(--radius-lg)",
      padding: pads,
      ...variants,
      ...style
    }
  }, rest), children);
}
Object.assign(__ds_scope, { Card });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/core/Card.jsx", error: String((e && e.message) || e) }); }

// components/core/Icon.jsx
try { (() => {
function _extends() { return _extends = Object.assign ? Object.assign.bind() : function (n) { for (var e = 1; e < arguments.length; e++) { var t = arguments[e]; for (var r in t) ({}).hasOwnProperty.call(t, r) && (n[r] = t[r]); } return n; }, _extends.apply(null, arguments); }
/**
 * Icon — a small, self-contained set of spare line glyphs.
 * No icon font, no CDN. Simple geometric strokes only; the set is
 * deliberately tiny to match the calm, uncluttered register.
 */

const P = {
  // navigational
  arrow: /*#__PURE__*/React.createElement("path", {
    d: "M4 10h12M11 5l5 5-5 5"
  }),
  "arrow-up-right": /*#__PURE__*/React.createElement("path", {
    d: "M6 14L14 6M7 6h7v7"
  }),
  chevron: /*#__PURE__*/React.createElement("path", {
    d: "M7 4l6 6-6 6"
  }),
  // content
  book: /*#__PURE__*/React.createElement("path", {
    d: "M4 4.5A1.5 1.5 0 015.5 3H16v13H5.5A1.5 1.5 0 004 17.5zM16 16v1.5"
  }),
  code: /*#__PURE__*/React.createElement("path", {
    d: "M7 6l-4 4 4 4M13 6l4 4-4 4"
  }),
  spark: /*#__PURE__*/React.createElement("path", {
    d: "M10 3l1.6 4.9L16.5 9l-4.9 1.6L10 15.5l-1.6-4.9L3.5 9l4.9-1.6z"
  }),
  dot: /*#__PURE__*/React.createElement("circle", {
    cx: "10",
    cy: "10",
    r: "2.5"
  }),
  // theme
  moon: /*#__PURE__*/React.createElement("path", {
    d: "M15.5 11.5A6 6 0 018.5 4.5 6 6 0 1015.5 11.5z"
  }),
  sun: /*#__PURE__*/React.createElement("g", null, /*#__PURE__*/React.createElement("circle", {
    cx: "10",
    cy: "10",
    r: "3.4"
  }), /*#__PURE__*/React.createElement("path", {
    d: "M10 2.5v2M10 15.5v2M2.5 10h2M15.5 10h2M4.7 4.7l1.4 1.4M13.9 13.9l1.4 1.4M15.3 4.7l-1.4 1.4M6.1 13.9l-1.4 1.4"
  })),
  // status
  check: /*#__PURE__*/React.createElement("path", {
    d: "M4 10.5l4 4 8-9"
  }),
  lantern: /*#__PURE__*/React.createElement("g", null, /*#__PURE__*/React.createElement("path", {
    d: "M8 3h4M10 3v2"
  }), /*#__PURE__*/React.createElement("rect", {
    x: "6.5",
    y: "5",
    width: "7",
    height: "10",
    rx: "2"
  }), /*#__PURE__*/React.createElement("path", {
    d: "M8.5 15v1.5M11.5 15v1.5"
  }))
};
function Icon({
  name = "dot",
  size = 18,
  strokeWidth = 1.6,
  label,
  style,
  ...rest
}) {
  const glyph = P[name] || P.dot;
  return /*#__PURE__*/React.createElement("svg", _extends({
    role: label ? "img" : "presentation",
    "aria-label": label,
    "aria-hidden": label ? undefined : true,
    width: size,
    height: size,
    viewBox: "0 0 20 20",
    fill: "none",
    stroke: "currentColor",
    strokeWidth: strokeWidth,
    strokeLinecap: "round",
    strokeLinejoin: "round",
    style: {
      display: "inline-block",
      flexShrink: 0,
      verticalAlign: "middle",
      ...style
    }
  }, rest), glyph);
}
const ICON_NAMES = Object.keys(P);
Object.assign(__ds_scope, { Icon, ICON_NAMES });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/core/Icon.jsx", error: String((e && e.message) || e) }); }

// components/core/Button.jsx
try { (() => {
function _extends() { return _extends = Object.assign ? Object.assign.bind() : function (n) { for (var e = 1; e < arguments.length; e++) { var t = arguments[e]; for (var r in t) ({}).hasOwnProperty.call(t, r) && (n[r] = t[r]); } return n; }, _extends.apply(null, arguments); }
/**
 * Button — the primary action. Three registers:
 *   primary   — the teal flame fill (one per view, ideally)
 *   secondary — outlined, quiet
 *   ghost     — text-only, for tertiary actions
 * Renders as <a> when `href` is set, otherwise <button>.
 */
function Button({
  children,
  variant = "primary",
  size = "md",
  href,
  icon,
  iconRight,
  disabled = false,
  style,
  ...rest
}) {
  const sizes = {
    sm: {
      padding: "0.4rem 0.75rem",
      fontSize: "var(--text-sm)",
      gap: "0.4rem"
    },
    md: {
      padding: "0.6rem 1.1rem",
      fontSize: "var(--text-base)",
      gap: "0.5rem"
    },
    lg: {
      padding: "0.8rem 1.5rem",
      fontSize: "var(--text-md)",
      gap: "0.6rem"
    }
  }[size];
  const variants = {
    primary: {
      background: "var(--accent)",
      color: "var(--text-on-flame)",
      border: "1px solid transparent",
      boxShadow: "var(--glow-flame-sm)"
    },
    secondary: {
      background: "transparent",
      color: "var(--text-strong)",
      border: "1px solid var(--border-strong)"
    },
    ghost: {
      background: "transparent",
      color: "var(--text-body)",
      border: "1px solid transparent"
    }
  }[variant];
  const base = {
    display: "inline-flex",
    alignItems: "center",
    justifyContent: "center",
    ...sizes,
    fontFamily: "var(--font-body)",
    fontWeight: "var(--weight-medium)",
    lineHeight: 1,
    borderRadius: "var(--radius-sm)",
    cursor: disabled ? "not-allowed" : "pointer",
    opacity: disabled ? 0.45 : 1,
    textDecoration: "none",
    whiteSpace: "nowrap",
    transition: "background var(--duration-fast) var(--ease-standard), border-color var(--duration-fast) var(--ease-standard), color var(--duration-fast) var(--ease-standard), transform var(--duration-fast) var(--ease-standard)",
    ...variants,
    ...style
  };
  const content = /*#__PURE__*/React.createElement(React.Fragment, null, icon && /*#__PURE__*/React.createElement(__ds_scope.Icon, {
    name: icon,
    size: size === "lg" ? 18 : 16
  }), children && /*#__PURE__*/React.createElement("span", null, children), iconRight && /*#__PURE__*/React.createElement(__ds_scope.Icon, {
    name: iconRight,
    size: size === "lg" ? 18 : 16
  }));
  const Tag = href && !disabled ? "a" : "button";
  return /*#__PURE__*/React.createElement(Tag, _extends({
    href: href && !disabled ? href : undefined,
    className: `syl-btn syl-btn--${variant}`,
    disabled: Tag === "button" ? disabled : undefined,
    "aria-disabled": disabled || undefined,
    style: base
  }, rest), content);
}
Object.assign(__ds_scope, { Button });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/core/Button.jsx", error: String((e && e.message) || e) }); }

// components/core/ExternalLink.jsx
try { (() => {
function _extends() { return _extends = Object.assign ? Object.assign.bind() : function (n) { for (var e = 1; e < arguments.length; e++) { var t = arguments[e]; for (var r in t) ({}).hasOwnProperty.call(t, r) && (n[r] = t[r]); } return n; }, _extends.apply(null, arguments); }
/**
 * ExternalLink — a text link that leaves the grounds. Carries a small
 * up-right arrow so outbound links (GitHub, docs, LinkedIn) read as such.
 * Prominent by design: GitHub links are load-bearing for Sylin.
 */
function ExternalLink({
  children,
  href,
  icon,
  muted = false,
  style,
  ...rest
}) {
  return /*#__PURE__*/React.createElement("a", _extends({
    href: href,
    className: "syl-extlink",
    target: "_blank",
    rel: "noopener noreferrer",
    style: {
      display: "inline-flex",
      alignItems: "center",
      gap: "0.35rem",
      color: muted ? "var(--text-muted)" : "var(--link)",
      fontFamily: "var(--font-body)",
      fontWeight: "var(--weight-medium)",
      ...style
    }
  }, rest), icon && /*#__PURE__*/React.createElement(__ds_scope.Icon, {
    name: icon,
    size: 16
  }), /*#__PURE__*/React.createElement("span", null, children), /*#__PURE__*/React.createElement(__ds_scope.Icon, {
    name: "arrow-up-right",
    size: 14,
    style: {
      opacity: 0.7
    }
  }));
}
Object.assign(__ds_scope, { ExternalLink });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/core/ExternalLink.jsx", error: String((e && e.message) || e) }); }

// components/core/PixelSprite.jsx
try { (() => {
function _extends() { return _extends = Object.assign ? Object.assign.bind() : function (n) { for (var e = 1; e < arguments.length; e++) { var t = arguments[e]; for (var r in t) ({}).hasOwnProperty.call(t, r) && (n[r] = t[r]); } return n; }, _extends.apply(null, arguments); }
/**
 * PixelSprite — renders pixel art crisply. Pixel art is the house
 * illustration medium; it must never be smoothed. This wrapper enforces
 * integer-friendly scaling and `image-rendering: pixelated`, and can add
 * the soft lantern glow the mascots sit within.
 */
function PixelSprite({
  src,
  alt = "",
  size = 100,
  scale,
  glow = false,
  style,
  ...rest
}) {
  const width = scale ? undefined : size;
  return /*#__PURE__*/React.createElement("img", _extends({
    src: src,
    alt: alt,
    className: "pixel syl-sprite",
    width: width,
    style: {
      imageRendering: "pixelated",
      width: scale ? `calc(var(--sprite-native, 100px) * ${scale})` : `${size}px`,
      height: "auto",
      display: "block",
      filter: glow ? "drop-shadow(0 0 18px rgba(51,183,200,0.35))" : undefined,
      ...style
    }
  }, rest));
}
Object.assign(__ds_scope, { PixelSprite });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/core/PixelSprite.jsx", error: String((e && e.message) || e) }); }

// components/core/Tag.jsx
try { (() => {
function _extends() { return _extends = Object.assign ? Object.assign.bind() : function (n) { for (var e = 1; e < arguments.length; e++) { var t = arguments[e]; for (var r in t) ({}).hasOwnProperty.call(t, r) && (n[r] = t[r]); } return n; }, _extends.apply(null, arguments); }
/**
 * Tag — a small, quiet keyword chip. For topics, tech, categories.
 * Not to be confused with MaturityLabel (which carries load-bearing
 * meaning about a project's state).
 */
function Tag({
  children,
  tone = "neutral",
  mono = false,
  style,
  ...rest
}) {
  const tones = {
    neutral: {
      color: "var(--text-muted)",
      borderColor: "var(--border-line)",
      background: "transparent"
    },
    accent: {
      color: "var(--accent-bright)",
      borderColor: "var(--accent-line)",
      background: "var(--accent-quiet)"
    }
  }[tone];
  return /*#__PURE__*/React.createElement("span", _extends({
    className: "syl-tag",
    style: {
      display: "inline-flex",
      alignItems: "center",
      padding: "0.15rem 0.55rem",
      fontFamily: mono ? "var(--font-mono)" : "var(--font-body)",
      fontSize: "var(--text-xs)",
      fontWeight: "var(--weight-medium)",
      letterSpacing: mono ? "0.02em" : "normal",
      lineHeight: 1.5,
      borderRadius: "var(--radius-xs)",
      border: "1px solid",
      ...tones,
      ...style
    }
  }, rest), children);
}
Object.assign(__ds_scope, { Tag });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/core/Tag.jsx", error: String((e && e.message) || e) }); }

// components/patterns/Callout.jsx
try { (() => {
function _extends() { return _extends = Object.assign ? Object.assign.bind() : function (n) { for (var e = 1; e < arguments.length; e++) { var t = arguments[e]; for (var r in t) ({}).hasOwnProperty.call(t, r) && (n[r] = t[r]); } return n; }, _extends.apply(null, arguments); }
/**
 * Callout — a short aside in the brand's candor register. Chief use:
 * "When not to use this" — candor is the brand, so this reads as a
 * feature, not a confession. Also serves notes and warmth.
 */
function Callout({
  children,
  title,
  tone = "candor",
  icon,
  style,
  ...rest
}) {
  const tones = {
    candor: {
      color: "var(--accent-bright)",
      accent: "var(--accent)"
    },
    note: {
      color: "var(--text-muted)",
      accent: "var(--border-strong)"
    },
    warm: {
      color: "var(--warm)",
      accent: "var(--warm)"
    }
  }[tone];
  return /*#__PURE__*/React.createElement("div", _extends({
    className: `syl-callout syl-callout--${tone}`,
    style: {
      display: "flex",
      gap: "var(--space-4)",
      padding: "var(--space-5)",
      background: "var(--surface-sunken)",
      borderRadius: "var(--radius-md)",
      borderLeft: `2px solid ${tones.accent}`,
      ...style
    }
  }, rest), icon && /*#__PURE__*/React.createElement("span", {
    style: {
      color: tones.color,
      flexShrink: 0,
      marginTop: 2
    }
  }, /*#__PURE__*/React.createElement(__ds_scope.Icon, {
    name: icon,
    size: 18
  })), /*#__PURE__*/React.createElement("div", {
    style: {
      display: "flex",
      flexDirection: "column",
      gap: "0.35rem"
    }
  }, title && /*#__PURE__*/React.createElement("span", {
    style: {
      fontFamily: "var(--font-mono)",
      fontSize: "var(--text-xs)",
      letterSpacing: "var(--tracking-label)",
      textTransform: "uppercase",
      color: tones.color
    }
  }, title), /*#__PURE__*/React.createElement("div", {
    style: {
      fontSize: "var(--text-sm)",
      color: "var(--text-body)",
      lineHeight: "var(--leading-normal)"
    }
  }, children)));
}
Object.assign(__ds_scope, { Callout });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/patterns/Callout.jsx", error: String((e && e.message) || e) }); }

// components/patterns/CommitmentCard.jsx
try { (() => {
function _extends() { return _extends = Object.assign ? Object.assign.bind() : function (n) { for (var e = 1; e < arguments.length; e++) { var t = arguments[e]; for (var r in t) ({}).hasOwnProperty.call(t, r) && (n[r] = t[r]); } return n; }, _extends.apply(null, arguments); }
/**
 * CommitmentCard — one of The Way's four commitments (Local-first, Radical
 * candor, Continuity, Agent-native). A quiet numbered panel: index, name,
 * and the plain-spoken promise beneath it.
 */
function CommitmentCard({
  index,
  name,
  children,
  icon = "lantern",
  style,
  ...rest
}) {
  return /*#__PURE__*/React.createElement("div", _extends({
    className: "syl-commitment",
    style: {
      display: "flex",
      flexDirection: "column",
      gap: "var(--space-3)",
      padding: "var(--space-6)",
      background: "var(--surface-card)",
      border: "1px solid var(--border-line)",
      borderRadius: "var(--radius-lg)",
      position: "relative",
      overflow: "hidden",
      ...style
    }
  }, rest), /*#__PURE__*/React.createElement("div", {
    style: {
      display: "flex",
      alignItems: "center",
      justifyContent: "space-between"
    }
  }, /*#__PURE__*/React.createElement("span", {
    style: {
      fontFamily: "var(--font-mono)",
      fontSize: "var(--text-xs)",
      letterSpacing: "var(--tracking-label)",
      color: "var(--text-faint)"
    }
  }, index), /*#__PURE__*/React.createElement("span", {
    style: {
      color: "var(--accent)",
      opacity: 0.8
    }
  }, /*#__PURE__*/React.createElement(__ds_scope.Icon, {
    name: icon,
    size: 20
  }))), /*#__PURE__*/React.createElement("h3", {
    style: {
      fontSize: "var(--text-xl)",
      fontFamily: "var(--font-display)",
      color: "var(--text-strong)"
    }
  }, name), /*#__PURE__*/React.createElement("p", {
    style: {
      fontSize: "var(--text-base)",
      color: "var(--text-body)",
      lineHeight: "var(--leading-normal)"
    }
  }, children));
}
Object.assign(__ds_scope, { CommitmentCard });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/patterns/CommitmentCard.jsx", error: String((e && e.message) || e) }); }

// components/patterns/MaturityLabel.jsx
try { (() => {
function _extends() { return _extends = Object.assign ? Object.assign.bind() : function (n) { for (var e = 1; e < arguments.length; e++) { var t = arguments[e]; for (var r in t) ({}).hasOwnProperty.call(t, r) && (n[r] = t[r]); } return n; }, _extends.apply(null, arguments); }
/**
 * MaturityLabel — honest, load-bearing state for a project. The three
 * tiers mean exactly what they say: Flagship (production-track), Growing
 * (working, earlier in its arc), Research (early, and honest about it).
 * `version` renders a mono chip (e.g. "v0.9 · pre-1.0"); pre-1.0 is stated
 * wherever it is true.
 */
function MaturityLabel({
  tier = "flagship",
  version,
  style,
  ...rest
}) {
  const tiers = {
    flagship: {
      label: "Flagship",
      color: "var(--tier-flagship)"
    },
    growing: {
      label: "Growing",
      color: "var(--tier-growing)"
    },
    research: {
      label: "Research",
      color: "var(--tier-research)"
    }
  }[tier];
  return /*#__PURE__*/React.createElement("span", _extends({
    style: {
      display: "inline-flex",
      alignItems: "center",
      gap: "0.5rem",
      ...style
    }
  }, rest), /*#__PURE__*/React.createElement("span", {
    className: `syl-maturity syl-maturity--${tier}`,
    style: {
      display: "inline-flex",
      alignItems: "center",
      gap: "0.4rem",
      padding: "0.2rem 0.65rem",
      borderRadius: "var(--radius-pill)",
      fontFamily: "var(--font-mono)",
      fontSize: "var(--text-2xs)",
      fontWeight: "var(--weight-medium)",
      letterSpacing: "var(--tracking-label)",
      textTransform: "uppercase",
      color: tiers.color,
      border: `1px solid color-mix(in oklab, ${tiers.color} 45%, transparent)`,
      background: `color-mix(in oklab, ${tiers.color} 12%, transparent)`
    }
  }, /*#__PURE__*/React.createElement("span", {
    "aria-hidden": "true",
    style: {
      width: 5,
      height: 5,
      borderRadius: "50%",
      background: tiers.color,
      boxShadow: `0 0 6px ${tiers.color}`
    }
  }), tiers.label), version && /*#__PURE__*/React.createElement("span", {
    style: {
      fontFamily: "var(--font-mono)",
      fontSize: "var(--text-xs)",
      color: "var(--text-muted)"
    }
  }, version));
}
Object.assign(__ds_scope, { MaturityLabel });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/patterns/MaturityLabel.jsx", error: String((e && e.message) || e) }); }

// components/patterns/ProjectCard.jsx
try { (() => {
function _extends() { return _extends = Object.assign ? Object.assign.bind() : function (n) { for (var e = 1; e < arguments.length; e++) { var t = arguments[e]; for (var r in t) ({}).hasOwnProperty.call(t, r) && (n[r] = t[r]); } return n; }, _extends.apply(null, arguments); }
/**
 * ProjectCard — the way a project is presented on the grounds. Every card
 * carries the three things a visitor needs in one scan: the what-it-is
 * sentence, the honest maturity state, and the get-started links (GitHub,
 * docs). The mascot accompanies the sentence — it never replaces it.
 */
function ProjectCard({
  name,
  kicker,
  sentence,
  tier = "flagship",
  version,
  mascot,
  links = [],
  featured = false,
  style,
  ...rest
}) {
  return /*#__PURE__*/React.createElement("article", _extends({
    className: "syl-project",
    style: {
      display: "flex",
      flexDirection: "column",
      gap: "var(--space-4)",
      padding: "var(--space-6)",
      background: "var(--surface-card)",
      border: `1px solid ${featured ? "var(--accent-line)" : "var(--border-line)"}`,
      borderRadius: "var(--radius-lg)",
      boxShadow: featured ? "var(--glow-flame-sm)" : "none",
      ...style
    }
  }, rest), /*#__PURE__*/React.createElement("header", {
    style: {
      display: "flex",
      alignItems: "flex-start",
      gap: "var(--space-4)"
    }
  }, mascot && /*#__PURE__*/React.createElement(__ds_scope.PixelSprite, {
    src: mascot,
    alt: `${name} mascot`,
    size: featured ? 64 : 48,
    glow: featured
  }), /*#__PURE__*/React.createElement("div", {
    style: {
      display: "flex",
      flexDirection: "column",
      gap: "0.15rem",
      flex: 1,
      minWidth: 0
    }
  }, kicker && /*#__PURE__*/React.createElement("span", {
    style: {
      fontFamily: "var(--font-mono)",
      fontSize: "var(--text-xs)",
      letterSpacing: "var(--tracking-label)",
      textTransform: "uppercase",
      color: "var(--text-muted)"
    }
  }, kicker), /*#__PURE__*/React.createElement("h3", {
    style: {
      fontSize: "var(--text-xl)",
      fontFamily: "var(--font-tool)",
      fontWeight: 600,
      letterSpacing: "-0.01em",
      color: "var(--text-strong)"
    }
  }, name))), /*#__PURE__*/React.createElement("p", {
    style: {
      fontSize: "var(--text-md)",
      color: "var(--text-body)",
      lineHeight: "var(--leading-normal)",
      flex: 1
    }
  }, sentence), /*#__PURE__*/React.createElement(__ds_scope.MaturityLabel, {
    tier: tier,
    version: version
  }), links.length > 0 && /*#__PURE__*/React.createElement("footer", {
    style: {
      display: "flex",
      flexWrap: "wrap",
      gap: "var(--space-4)",
      paddingTop: "var(--space-3)",
      borderTop: "1px solid var(--border-hairline)"
    }
  }, links.map((l, i) => /*#__PURE__*/React.createElement(__ds_scope.ExternalLink, {
    key: i,
    href: l.href,
    icon: l.icon
  }, l.label))));
}
Object.assign(__ds_scope, { ProjectCard });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/patterns/ProjectCard.jsx", error: String((e && e.message) || e) }); }

// components/patterns/Stat.jsx
try { (() => {
function _extends() { return _extends = Object.assign ? Object.assign.bind() : function (n) { for (var e = 1; e < arguments.length; e++) { var t = arguments[e]; for (var r in t) ({}).hasOwnProperty.call(t, r) && (n[r] = t[r]); } return n; }, _extends.apply(null, arguments); }
/**
 * Stat — a single value-as-fact. Sylin's stats are proof, not decoration:
 * "Accounts required: 0 · Telemetry sent: none · Decisions documented:
 * 300+ ADRs · Clouds involved: 0." Big mono number, quiet label.
 */
function Stat({
  value,
  label,
  accent = false,
  style,
  ...rest
}) {
  return /*#__PURE__*/React.createElement("div", _extends({
    style: {
      display: "flex",
      flexDirection: "column",
      gap: "0.25rem",
      ...style
    }
  }, rest), /*#__PURE__*/React.createElement("span", {
    style: {
      fontFamily: "var(--font-mono)",
      fontSize: "var(--text-2xl)",
      fontWeight: "var(--weight-medium)",
      lineHeight: 1,
      color: accent ? "var(--accent-bright)" : "var(--text-strong)",
      fontVariantNumeric: "tabular-nums"
    }
  }, value), /*#__PURE__*/React.createElement("span", {
    style: {
      fontFamily: "var(--font-mono)",
      fontSize: "var(--text-xs)",
      letterSpacing: "var(--tracking-label)",
      textTransform: "uppercase",
      color: "var(--text-muted)"
    }
  }, label));
}

/**
 * StatRow — lays out a set of Stats with hairline dividers between them,
 * the way the "values-as-stats" line reads on the hub.
 */
function StatRow({
  stats = [],
  style,
  ...rest
}) {
  return /*#__PURE__*/React.createElement("div", _extends({
    style: {
      display: "flex",
      flexWrap: "wrap",
      gap: "var(--space-6)",
      alignItems: "flex-start",
      ...style
    }
  }, rest), stats.map((s, i) => /*#__PURE__*/React.createElement(React.Fragment, {
    key: i
  }, i > 0 && /*#__PURE__*/React.createElement("span", {
    "aria-hidden": "true",
    style: {
      width: 1,
      alignSelf: "stretch",
      background: "var(--border-line)"
    }
  }), /*#__PURE__*/React.createElement(Stat, {
    value: s.value,
    label: s.label,
    accent: s.accent
  }))));
}
Object.assign(__ds_scope, { Stat, StatRow });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/patterns/Stat.jsx", error: String((e && e.message) || e) }); }

__ds_ns.Button = __ds_scope.Button;

__ds_ns.Card = __ds_scope.Card;

__ds_ns.ExternalLink = __ds_scope.ExternalLink;

__ds_ns.Icon = __ds_scope.Icon;

__ds_ns.ICON_NAMES = __ds_scope.ICON_NAMES;

__ds_ns.PixelSprite = __ds_scope.PixelSprite;

__ds_ns.Tag = __ds_scope.Tag;

__ds_ns.Callout = __ds_scope.Callout;

__ds_ns.CommitmentCard = __ds_scope.CommitmentCard;

__ds_ns.MaturityLabel = __ds_scope.MaturityLabel;

__ds_ns.ProjectCard = __ds_scope.ProjectCard;

__ds_ns.Stat = __ds_scope.Stat;

__ds_ns.StatRow = __ds_scope.StatRow;

})();
