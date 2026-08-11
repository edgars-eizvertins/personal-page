# Design: the token contract

The stylesheet is split in two, and the split is the whole point:

| File | Contains | Rule |
| --- | --- | --- |
| `wwwroot/css/tokens.css` | Custom properties, nothing else | Every declaration is a `--variable` |
| `wwwroot/css/site.css` | Layout, typography, components | Consumes only those variables |

If a literal colour or a magic pixel value appears in `site.css`, that is a bug: it would survive
a design swap and quietly contradict the new palette.

Applying a design is therefore replacing one file, and then adjusting spacing and layout in the
other. Budget real time for that second half — a pure token swap is the ideal case, and any real
design also wants layout changes.

## What ships today

`tokens.css` is a deliberately plain placeholder: system fonts, a neutral grey scale, one
restrained blue accent. It should look unfinished but never be structurally wrong. The engine was
built against it on purpose, so that generating a design could be done against real rendered
HTML rather than a description of it.

## The variables

```
Colour
  --color-bg           --color-surface       --color-text        --color-text-muted
  --color-accent       --color-accent-hover  --color-border      --color-code-bg

Type
  --font-sans          --font-mono
  --text-xs  --text-sm  --text-base  --text-lg  --text-xl  --text-2xl  --text-3xl
  --line-height-body   --line-height-heading

Spacing (one scale)
  --space-1 --space-2 --space-3 --space-4 --space-5 --space-6 --space-8 --space-10 --space-12

Shape
  --radius-sm  --radius-md  --radius-lg
  --shadow-sm  --shadow-md

Layout
  --measure            max line length for body text, around 68ch
  --page-width         outer content column

Syntax highlighting
  --syntax-comment     --syntax-keyword      --syntax-string     --syntax-number
  --syntax-function    --syntax-type         --syntax-operator   --syntax-punctuation
  --syntax-attribute   --syntax-builtin      (extras, see below)
```

`--syntax-attribute` and `--syntax-builtin` go beyond the eight categories the design brief asks
for, because ColorCode distinguishes them and a YAML key coloured as a type reads oddly. They are
consumed with a fallback:

```css
color: var(--syntax-attribute, var(--syntax-type));
```

So a design that supplies only the eight required tokens still renders correctly.

## Both themes, declared twice

Each theme appears in `tokens.css` twice:

```css
:root                                    { /* light  */ }
@media (prefers-color-scheme: dark) {
  :root                                  { /* dark   */ }
}
:root[data-theme="light"]                { /* light  */ }
:root[data-theme="dark"]                 { /* dark   */ }
```

The media query alone cannot express "this visitor overrode their OS setting", which is exactly
what the header toggle needs. The attribute selectors are written after the media query and are
more specific, so they win **in both directions** — a visitor on a dark-mode OS who picks light
gets light.

This duplication is not an accident to be tidied away. Collapsing it breaks the toggle.

## Swapping in a design

1. Generate the design from [design-brief.md](design-brief.md). Tell whoever or whatever
   generates it that **the markup is fixed and must be styled as given** — otherwise the result
   assumes a scaffold of wrapper elements this engine does not emit.
2. Replace the values in `tokens.css`. Keep every variable name; add new ones freely.
3. Run the site and read a long blog post, the experience timeline, and a project detail page.
4. Adjust `site.css` for spacing and layout. Resist adding literals.
5. Check both themes and the OS-preference path with the toggle set to "system".
6. Check contrast. WCAG AA in both themes, including the syntax palette against
   `--color-code-bg`.
7. Check 360px width. Content column, tables and code blocks all have to survive it.

## Class names the design has to style

These are what the engine actually emits. Nothing else exists.

| Area | Classes |
| --- | --- |
| Shell | `.site-header`, `.site-header-inner`, `.site-title`, `.site-main`, `.site-footer`, `.site-footer-inner`, `.site-footer-links`, `.wrap`, `.skip-link`, `.visually-hidden` |
| Nav | `.site-nav`, `.site-nav-list`, `.site-nav-link`, `.site-nav-link.active`, `.nav-toggle`, `.nav-toggle-label`, `.nav-toggle-bars`, `.nav-toggle-text` |
| Theme toggle | `.theme-toggle`, `.theme-option`, `[aria-pressed]` |
| Page | `.page`, `.page-header`, `.page-title`, `.page-meta`, `.page-summary`, `.page-links`, `.section-title`, `.home-section` |
| Article | `.prose` and the elements inside it — `h2`–`h6`, `p`, `ul`, `ol`, `blockquote`, `table`, `hr`, `img`, `code` |
| Code | `.code-block`, `.code-block[data-language]`, `.code-block pre`, `.code-block code`, `.code-copy`, `.code-copy[data-copied]` |
| Cards | `.card-grid`, `.card`, `.card-image`, `.card-body`, `.card-title`, `.card-date`, `.card-summary`, `.card-links` |
| Blog list | `.post-list`, `.post-row`, `.post-row-date`, `.post-row-title`, `.post-row-summary`, `.post-footer-nav` |
| Timeline | `.timeline`, `.timeline-item`, `.timeline-marker`, `.timeline-body`, `.timeline-role`, `.timeline-company`, `.timeline-meta`, `.timeline-dates`, `.timeline-location` |
| Tags | `.tag-list`, `.tag` |
| Diagnostics | `.diagnostics-section`, `.diagnostics-list`, `.diagnostics-empty` |

## The syntax palette

Highlighting happens on the server, so the class names inside a code block are ColorCode's scope
reference names, not a JavaScript library's. `site.css` maps them onto the syntax tokens:

| Token | ColorCode classes mapped to it |
| --- | --- |
| `--syntax-comment` | `comment`, `htmlComment`, `xmlComment`, `xmlDocComment` |
| `--syntax-keyword` | `keyword`, `controlKeyword`, `preprocessorKeyword`, `pseudoKeyword` |
| `--syntax-string` | `string`, `stringCSharpVerbatim`, `jsonString`, `htmlAttributeValue`, `xmlAttributeValue` |
| `--syntax-number` | `number`, `jsonNumber` |
| `--syntax-function` | `builtinFunction`, `constructor`, `powershellCommand`, `sqlSystemFunction`, `markdownHeader` |
| `--syntax-type` | `type`, `className`, `typeVariable`, `namespace`, `predefined`, `powershellType`, `powershellVariable`, `htmlElementName`, `xmlName`, `cssSelector` |
| `--syntax-operator` | `operator`, `htmlOperator`, `powershellOperator`, `specialChar`, `stringEscape` |
| `--syntax-punctuation` | `delimiter`, `htmlTagDelimiter`, `xmlDelimiter`, `brackets` |
| `--syntax-attribute` | `attribute`, `jsonKey`, `htmlAttributeName`, `xmlAttribute`, `cssPropertyName`, `powershellParameter`, `powershellAttribute` |
| `--syntax-builtin` | `builtinValue`, `jsonConst`, `htmlEntity`, `cssPropertyValue` |

## Constraints a design cannot negotiate away

- **System or web-safe fonts only.** No Google Fonts, no font CDN. The site must work on a
  network with no route to the internet, and the CSP forbids external hosts anyway.
- **No external anything.** No CDN scripts, no analytics, no third-party widgets. A library, if
  one is genuinely needed, is vendored into `wwwroot` and committed.
- **Fully usable with JavaScript off.** Every page renders, reads and navigates without script.
  The mobile nav is a CSS-only disclosure (hidden checkbox plus label plus sibling selector); the
  theme toggle is the one control that needs script, and it stays hidden until script unhides it.
- **One `<h1>` per page.** The layout renders it from front matter; article bodies start at
  `<h2>`. Style `.page-title` as the page-title element and `.prose h2`–`.prose h4` as in-article
  headings.
- **Highlighting is server-side.** The palette is part of the token set, not a stylesheet
  shipped with a highlighting library.
