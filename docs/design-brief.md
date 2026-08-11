# Design brief

Everything below the horizontal rule is a prompt template for generating the site's visual
design (in a design tool, an LLM, or by hand). Fill in the `<angle bracket>` placeholders with
your own details, then use it as-is.

**Why the token names matter:** the site's CSS is split into `tokens.css` (custom properties
only) and `site.css` (layout, consuming only those properties). If the design comes back using
the exact variable names listed below, applying it is a copy-paste into `tokens.css` with no
rewiring. If it uses different names, someone has to map them by hand.

---

I need a design system for my personal developer portfolio site.

I'm a <your role, e.g. backend/web developer> with around <N> years of experience in <your main
stack>. The site is server-rendered HTML with no JavaScript framework, and all content is
written in markdown, so **long-form reading comfort matters more than visual effects**.

## Tone

Calm, confident, engineer-like. Substance over decoration. It should feel like it was made by
someone who cares about craft, not like a template someone bought.

Please avoid: gradient hero blobs, glassmorphism, floating 3D shapes, oversized hero text, and
generic startup-landing-page energy. Think closer to a well-set technical publication than a
SaaS marketing site. One restrained accent colour, used sparingly for links and emphasis rather
than splashed across everything.

## Pages to design

1. **Home** — short intro paragraph, then a list of recent projects and recent blog posts
2. **Experience** — a vertical timeline of roles; each entry has company, job title, date range,
   location, and a row of technology tags
3. **Projects** — a card grid, plus a single-project detail page
4. **Blog** — a chronological post list showing date, title, summary and tags, plus a
   single-post reading page
5. **Simple prose pages** — About, Skills, Education, Now, Uses, Contact. These are pure
   markdown with no special layout, so the article style below does all the work.

## Components to specify

- Header / nav — horizontal, only a handful of items, must collapse sensibly on mobile
- Theme toggle control — sits in the header, three states (system / light / dark)
- Footer — minimal, with a few social links
- Timeline entry for the experience page
- Project card — title, one-line summary, tag pills, optional thumbnail image
- Blog list row — date, title, summary, tags
- Tag pill — default and interactive states
- Copy-to-clipboard button on fenced code blocks — resting, hover, and "copied" states
- **The full long-form markdown article style**, which is the most important piece:
  h1 through h4, paragraphs, links, ordered and unordered lists, blockquote, inline code,
  fenced code blocks with syntax highlighting, tables, horizontal rules, and images with
  captions

Note on headings: the page title comes from metadata and is rendered as the `<h1>` by the layout,
outside the article body. Article content therefore starts at `<h2>`. Please design the h1 as a
page-title element and h2–h4 as in-article headings.

## Hard requirements

**Light and dark themes**, both properly designed. Dark must not be a mechanical inversion of
light.

Please declare each theme **twice**: once under `@media (prefers-color-scheme: dark)` so the OS
setting works with no script, and once under `:root[data-theme="dark"]` (with a matching
`:root[data-theme="light"]`) so a manual toggle can override the OS in either direction. The
attribute selectors must win over the media query both ways — a visitor on a dark-mode OS who
picks light must get light.

**Deliver the design as CSS custom properties**, using exactly these names:

```
Colour
  --color-bg           --color-surface       --color-text        --color-text-muted
  --color-accent       --color-accent-hover  --color-border      --color-code-bg

Type
  --font-sans          --font-mono
  --text-xs  --text-sm  --text-base  --text-lg  --text-xl  --text-2xl  --text-3xl
  --line-height-body   --line-height-heading

Spacing (one consistent scale)
  --space-1 --space-2 --space-3 --space-4 --space-5 --space-6 --space-8 --space-10 --space-12

Shape
  --radius-sm  --radius-md  --radius-lg
  --shadow-sm  --shadow-md

Layout
  --measure            (max line length for body text, around 68ch)
```

Code blocks are highlighted **on the server**, so the highlighting palette also has to be part of
the token set rather than coming from a JavaScript library's stylesheet. Please include tokens for
the usual syntax categories — comment, keyword, string, number, function, type, operator,
punctuation — designed for both themes and meeting the contrast requirement below against
`--color-code-bg`.

**System font stack or web-safe fonts only.** No Google Fonts, no external font CDN — the site
runs on a small self-hosted server on a private network and must not depend on any outside
resource.

**WCAG AA contrast** in both themes.

**Responsive from 360px to 1440px.** Content column centred with a comfortable reading measure.

**The page must be fully readable and navigable with JavaScript disabled.** Everything is
server-rendered HTML. A small amount of hand-written vanilla JS is fine as an *enhancement* on
top — no framework, no CDN, no third-party script, since the site runs on a network that may have
no route to the internet. Please build the mobile nav toggle as a CSS-only pattern (hidden
checkbox plus `<label>`, or `:target`) and specify its markup and states as part of the
header/nav component.

**A light/dark theme toggle in the header.** Three states: follow the OS setting (the default),
force light, force dark. Please design the control itself, and note that it works by setting a
`data-theme` attribute on `<html>` — which is why the tokens are declared the way the next
section describes.

## Deliverables

- The complete token set for both light and dark themes
- The component specs listed above
- A sample rendering of a blog post page and of the experience timeline, so I can see the type
  scale working against real content rather than in isolation
