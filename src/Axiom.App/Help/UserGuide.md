<!-- Generated from the maintained Axiom v1 User Guide. Source SHA-256: 8186B3C0756AC2BC82DC2F89A7D9927D56EFFADEFEE3E69838D00C21103A8D74. Edit the maintained guide and run scripts/Sync-OfflineHelp.ps1. -->

# Axiom User Guide

Current feature guidance for the Windows application. Help describes available behavior and its limits; release acceptance and historical build evidence are maintained separately in Operations. Use Help topics or Search Help text to find a feature. This guide is bundled offline with each build.


## Find Help for every feature

Open **Main menu → Help → Browse all feature topics**, use the topic selector, or search words from a feature's name. The Help toolbar icon and F1 open the same offline guide. The main Help panel also includes **All commands and default shortcuts**, generated from the running command catalog and linked to feature instructions. Customized bindings are shown in Keyboard Shortcuts, not in that default reference.

| Feature group | Help topics to look for |
| --- | --- |
| Documents and collections | Open and edit; Manage documents and collections; Work with document tabs; Recent documents; Pin important documents |
| Workspace navigation | Workspace navigation and safe moves; Hierarchical outlines and advanced tools; Navigate by document outline; Search the workspace; Discovery and embedded Git terminal |
| Editing | Lists, field information and C# highlighting; Authoring assistance; Daily authoring; Save shortcuts; Settings and editor preferences |
| Markdown and Preview | Structured Markdown and wiki navigation; Follow Markdown links; Incoming Markdown references; Preview reliability; Preview navigation and synchronized scrolling |
| Identity and metadata | Axiom identity; Document metadata and classification; Explicit relationships |
| Recovery and conflicts | Recovery; Deleted documents and recorded folder structure; External changes |
| Layout and commands | Compact workbench; Toolbar and viewing modes; Command palette; Keyboard shortcut settings; Command workflows and cancellation |
| History and Git | Document history, comparison and restore; Git Changes; Discovery and embedded Git terminal |
| Export | Export to Word and HTML; Plain-text export; PDF export; EPUB and OpenDocument Text export |
| Images and templates | Drop files and import assets; Templates and named creation; Authoring assistance |
| Connected reading | CP connection and reviewed analysis; Local CP controls; Semantic search: step-by-step test; Read-only browser and phone companion |
| Maintenance and build | Diagnostics, backups, and versions; Per-user installation and trusted updates; Identify the running build; Platform state compatibility |

**CP connection Help** and **Semantic search test walkthrough** also have direct entries under Main menu → Help. Both CP review screens have a Help button and support F1. Topics describe current behavior and supported limits; historical release records are maintained outside the in-app guide.

## Preview reliability and protected recovery

Large images and documents use a private temporary HTML file when Preview exceeds the native browser's string-loading budget. Your Markdown and image files remain unchanged. Only the exact generated file is allowed as an internal navigation target; external navigation restrictions still apply. Normal close removes this view's generated files. An unexpected exit can leave temporary HTML in the app's cache.

Preview failures display a persistent status message while leaving the editor, Undo and recovery available. Reopening or switching the document retries Preview. After an unexpected exit, choose **Restore** if offered to recover protected unsaved text without overwriting its saved file. Open the portable **Axiom.App.exe** to use your existing app state; **Test-Axiom.cmd** uses separate test state.

## Lists, field information and C# highlighting

Metadata has an information button immediately beside Tags, Type and Aliases. Each explains that field; information stays available when the current document is not eligible for editing. Classify filters and Workspace Search have the same label-level help, and relationship editing explains Target, Kind and Optional note. Apply and Cancel retain their existing meaning; reading information does not change metadata or Markdown.

Tab on a list item nests it using the preceding parent's content column, including the extra space needed for ordered markers such as 10. A new ordered child starts at 1. Shift+Tab returns it to its parent level and sequence. Enter still continues the current marker; Shift+Enter inserts an explicit hard break. List-specific transformations skip fenced examples and the document identity line; ordinary code editing keeps its normal behavior. These changes are ordinary editor edits and undo in one step.

Fences labelled csharp, c# or cs receive offline C# colouring in the editor and Preview: keywords, strings, comments and numbers. Colour follows Light/Dark/System appearance. This is lexical highlighting, without compilation, execution or diagnostics; other languages keep plain code. Individual code blocks above 1 MiB or beyond the token budget fall back to uncoloured source, preserving content. Export uses the existing readable code representation and does not promise syntax colours in every format.

Right-click the editor for six groups: History (Undo/Redo), Clipboard, Inline formatting, Paragraphs and headings, Lists, and Insert. Spelling suggestions appear as an additional group when applicable. Commands retain the captured selection, availability reasons and normal Undo behavior.

Known callouts (note, info, tip, important, warning, caution and danger) show a representative type icon and a title in Preview. The source marker is hidden from the rendered title/body, including quotes written without a space after >. A custom title is preserved; otherwise the type name is used. Body formatting, lists and nested quotes remain intact. Unknown types remain ordinary quoted text. Source Markdown retains its marker for portability; existing export representations are unchanged.

## Windows keyboard and pointer interaction

The compact title bar keeps the document title in its own region and provides Minimize, Maximize/Restore and Close buttons. Settings can switch back to the standard title bar. Inactive tabs have a distinct boundary and background; the horizontal scrollbar reserves its own row below the tabs.

Help topics and search remain above the hosted Help content and accept mouse input. In the workspace tree, drag a file or folder onto its destination folder and review the move before continuing. Cancelling leaves the source and unsaved buffers unchanged; a move involving unsaved descendants still requires the normal save/discard decision. Refreshing the tree reveals the active document and its parent folders without clearing your filter. Show in Explorer selects the actual requested item, including names with spaces or commas. The workspace context menu and command palette also provide Show workspace folder in Explorer to reveal the root folder itself.

## Toolbar and viewing modes

Choose Main menu > Tools > Customize Toolbar, or use the application.toolbar command. Check commands to show them, select a row and choose Move up/Move down to reorder, then Apply. Cancel leaves the saved configuration unchanged; Reset restores the default command choices/order when you Apply. Settings also offers Configure toolbar; that dialog changes the pending Settings choice until you Apply Settings. The main menu remains available even if you hide every optional toolbar action. Hidden actions remain accessible through the command palette, menu and configured shortcuts. Enrollment and Pin still depend on the current document/workspace.

Tools > Viewing modes provides Full screen (F11 toggles), Zen / distraction-free, Presentation, Compact and Exit focused/full-screen view. Zen concentrates on the editable source; Presentation concentrates on Preview and supplies Previous page/Next page buttons. Both hide side panels, tabs and the top toolbar while keeping an obvious Exit view — Esc control and the footer with unsaved/protected status. Escape works from the editor and hosted Preview. Full screen changes the native window state and preserves the workbench. Exit restores the preceding window/layout and reading mode. Source, caret, Undo and recovery remain intact; returning from Presentation restores the editor position. Focused/full-screen modes are temporary and restart in the normal layout.

Compact is a separate remembered spacing preference, also available in Settings. It reduces toolbar padding and tab text size while retaining command access. Exit view retains that preference; toggle Compact again or turn off Compact workbench spacing to restore normal density. Presets are the initial supported design: Zen and distraction-free share one mode, and Presentation navigation scrolls the current document. Moving arbitrary toolbar groups into different quadrants is deferred for design review.



## Git Changes: managed saved-file workflow

Workspace Git opens the Git Changes tool panel in the main workbench. Use its branch icon in the right rail or the workspace.git command. Repository details and limits expand on demand. Select changed saved Markdown files to review Saved diff (HEAD against disk) or Staged diff (HEAD against index). The read-only comparison remains beside the editor and shows added/removed/unchanged lines, both line numbers and newline markers. Refresh clears old diff output and loads current Git status.

Changes lists supported saved Markdown changes to stage. Staged Changes lists supported index entries to commit or unstage. Fully staged files move to Staged Changes and remain selected for committing; a file with further saved edits appears in both lists. Previously selected staged files stay selected after Refresh. Review the selected staged paths in the final confirmation. Unsupported entries appear in the non-selectable Requires external Git section with their reasons.

Stage/Unstage operates only on explicitly selected supported saved Markdown files, with review confirmation. Selected staging is a single reviewed batch: cancellation or a failed review before publication leaves the index unchanged. Dirty or conflicted editor buffers cannot be staged or committed. You can continue editing while the panel is open; availability updates when a buffer changes. Enter a commit message in the panel, choose Commit selected staged or Ctrl+Enter, and review the final branch/files/message confirmation. Cancel preserves HEAD. A selected-only local commit retains unrelated staging and working files. No automatic Save occurs.

Close or Escape collapses the panel and returns editor focus; Escape during a Git operation cancels that operation. Completed operations remain completed, so refresh before retrying a cancelled sequence. Switching/forgetting the workspace clears the stale panel and cancels its pending operation; reopened Git inspects the current workspace. Panel choice/width use the existing workbench layout settings. If a restored Git panel has no current snapshot, choose Open workspace Git.

The managed Changes tab covers local changes, saved/staged diffs, deliberate stage/unstage and unsigned selected-file commit. Git must be installed. Remote operations, branch switching, conflict resolution, filtered/LFS files, submodules, renames and sparse checkout remain outside this supported workflow. Existing repository policy checks and disabled hooks/filters remain visible in the review. Axiom history checkpoints remain separate from branch commits.


## Authoring assistance: spelling, images, symbols and callouts

Ordinary Enter retains normal Markdown behavior. Shift+Enter inserts a standard Markdown hard break using two trailing spaces and the document's newline convention; inside fenced code it inserts a literal newline. One Undo reverses it. The editor context menu includes existing formatting plus table, horizontal rule, note callout, hard break, clipboard actions and symbol insertion. Right-click inside a selection retains it; keyboard invocation uses the current selection/caret. The identity line remains protected from these formatting operations.

Settings offers a subtle current-line highlight (on by default), non-printing spaces/tabs/line-ending markers (off by default), offline English (US) spell-check, a personal dictionary, optional Go to editor navigation and corresponding source block-start line numbers. Ctrl+Shift+N toggles saved non-printing-character display. These markers never change document bytes. Go to editor replaces the former Source label and navigates to the corresponding dirty Markdown block; line numbers refer to actual source lines, including hidden identity metadata, and navigation annotations stay out of exports.

Spelling underlines appear after typing pauses. Right-click a flagged word for suggestions, Ignore for this session or Add to personal dictionary. Settings lets you edit/remove personal words, one per line, up to 1,000 words. Code, link destinations, URLs, email addresses and identity metadata are excluded. Checking is local and uses a bundled English dictionary. To keep editing responsive, checking is bounded to documents up to one million characters and 1,000 displayed issues. Suggestions are optional; they never automatically alter text.

Ctrl+V pastes ordinary clipboard text. When there is no text format, a supported bitmap is copied as a collision-safe PNG into an assets folder beside the saved workspace document, and a relative Markdown image reference is inserted at the caret/selection. Paste clipboard image in the context menu/command palette explicitly selects the bitmap even when the clipboard also has text. Save the document inside the workspace first. Assets are limited to 16 million pixels and 20 MiB. Failed imports do not insert broken references. Markdown stays unsaved; Undo removes the reference and retains the asset. Review unused assets manually before deleting them. Alt text remains normal editable Markdown.

Insert symbol or emoji opens a searchable local collection of Unicode symbols and emoji. Search by name, choose an item and press Enter; Escape cancels. Multi-codepoint symbols remain intact and one Undo reverses insertion. Appearance and color depend on installed fonts and other readers; the picker does not promise interchangeable filled/outlined/color artwork.

Portable callouts use quoted Markdown, for example:

```markdown
> [!note] A useful title
> Body with **formatting**.
```

The initial titled set is note, info, tip, warning and danger. Preview styles these callouts; ordinary Markdown readers and exports retain readable quotation text. Unknown titled types remain ordinary quotations. GitHub-style standalone alert markers are also supported. Nested quotations remain source Markdown. Collapsible Obsidian suffixes are not implemented in this first version.

Mermaid diagrams work offline in Preview using fenced mermaid code:

````markdown
```mermaid
flowchart LR
    Write --> Preview
```
````

The bundled renderer is Mermaid 11.16.1. Valid diagrams show a graphic with expandable Diagram source; invalid or unsupported diagrams show an error and readable source. Rendering uses strict mode, disables document configuration, HTML labels and external resources, and limits rendering to eight diagrams, 20,000 characters per diagram, 300 edges and a ten-second asynchronous deadline. Light/Dark refresh retains source and navigation. All six exports (HTML, PDF, Word, EPUB, ODT and text) currently preserve readable Mermaid source and report an explicit warning; they do not export diagram graphics.  Source/recovery are never silently saved or rewritten by diagram rendering.


## Compact workbench and window controls

The main toolbar uses a consistent outline icon family with transparent idle backgrounds. The hamburger opens File, Edit, Format and Tools. Alt or F10 focuses it; Alt+F, Alt+E and Alt+M open those sections. The command palette and your customized shortcuts remain available. Quiet and Rounded menu styles remain in Settings.

On Windows, the toolbar shares a compact title region while retaining system minimize, maximize/restore and close buttons. Drag the blank title area or double-click it to maximize/restore. Settings → **Compact title bar (Windows)** can restore the standard system title bar; this preference persists. Appearance (System/Light/Dark) is in Settings and remains available through the command palette.

The Workspace Files header has a magnifier for the filename filter and an actions button for Open, remembered workspaces, Refresh, New document, New folder, bulk enrollment and Workspace Help. Ctrl+Shift+L opens and focuses the filename filter. Ctrl+Shift+F still opens full-text workspace search. Escape hides the filename filter and preserves its query; an active-filter status remains visible. Open the filter and choose Clear to remove the query. Open document tabs use less vertical space while retaining dirty indicators, close targets, horizontal overflow and context actions.

Disabled navigation and image commands provide their unavailable reason through a hover/focus host; keyboard focus reports the reason in the footer, and commands remain unable to execute. Metadata refresh shows local progress and completed/unchanged/error feedback with saved-file and eligible-identity counts. It does not save or enroll your current buffer. The info icon beside Metadata opens optional tags/type/alias guidance and a link to Metadata Help; eligibility instructions and errors remain visible.

## Workspace navigation and safe moves

Opening a saved workspace document or selecting its tab selects the canonical file in Workspace Files and expands its parent folders. This retains keyboard focus and does not expand the file's heading outline. A filter stays in place; the filter status explains when it hides the active document.

New (Ctrl+N) opens an unsaved buffer with the selected folder as its eventual Save default. A selected file or heading uses its parent folder; no selection uses the workspace root. The status shows the intended folder. Nothing is created until you explicitly Save. Save As starts in the saved document's current folder. The template workflow also starts its folder picker at the current selection.

Pinned files appear as a compact **Pinned** group at the top of Workspace Files, with a star on the canonical file as well. Click an entry to open its existing tab. Right-click a file to pin or unpin it without changing the active editor. The folder hierarchy remains below the pins.

Right-click a file, folder or document tab for actions on that specific item. Tabs target their own document even when inactive. **Copy saved workspace file for paste** captures the saved Markdown bytes for **Paste copied workspace file** inside Axiom; unsaved editor changes are not included. Paste asks for a destination and confirmation, never overwrites, and gives a copied Axiom document a distinct identity. **Copy workspace item path** copies plain path text to the system clipboard. Folder copy/recycle are unavailable; folders support New, Rename and Move. **Show workspace item in Explorer** selects the existing item in Windows Explorer.

Drag a workspace file or folder onto another folder, or onto the blank explorer area for the workspace root. The target folder is highlighted; dropping requests explicit confirmation. Dirty documents affected by a move require Save, Discard or Cancel. Cancel retains the source and dirty buffer. Folder moves include every descendant, including images, non-Markdown files and empty folders. Links inside a moved subtree can remain relative; references outside it may need manual repair. Axiom does not rewrite links automatically.

Moves reject linked files/folders, cycles, root escape, existing destinations and case-only renames. Folder review supports up to 10,000 entries. Move receipts protect restart reconciliation: paths for pins, document identities, protected drafts, recent files and remembered tabs are updated transactionally. A filesystem move followed by a state failure blocks more file operations until restart reconciliation. If the moved subtree changed before reconciliation, Diagnostics reports that it needs inspection; Axiom does not repeat the filesystem move or guess which version is correct.

## Wrapping, focus, scrolling and diagnostics

Wrap long editor lines in Settings to fit text within the editor. Wrapped visual rows retain the original Markdown line number; switching wrapping does not insert line breaks. With wrapping off, Enter in a long list reveals the full marker of the continued list item.

Scroll synchronization follows editor scrolling to Preview and Preview scrolling to the editor. Turn it off to read each pane independently. Typing retains the Preview position unless synchronized editor scrolling needs to follow the writing position. Refreshing Preview does not intentionally move keyboard focus.

Help opens with focus in Search Help text. Type a term to find headings and passages, then return to the editor to keep writing with Help open. Commands that return to the editor focus its typing area.

Opening Axiom again with the same application-state root activates the existing window. Opening a Markdown file from a second launch forwards it to that window and reuses an existing tab where applicable; dirty tabs stay protected. Separate explicitly configured state roots remain available for isolated testing and preserved versions. If activation fails, close the existing instance normally after preserving unsaved work and retry. A second writer is not started automatically.

Tools → Diagnostics shows readable timestamps, severity, actions and reported problems. Use Show/hide technical details to inspect the original log records. Refresh reloads the bounded log tail; Copy copies the displayed view. For a file-lock report, preserve unsaved work and close competing Axiom windows normally before retrying.

## Hierarchical outlines and advanced tools

Workspace Files shows only Markdown content. A folder appears when it is genuinely empty or contains a `.md` or `.markdown` document directly or anywhere in its subfolders; Axiom keeps every parent needed to reach that document. Genuinely empty folders remain visible. Populated branches containing only other file types stay hidden. Adding or removing the last Markdown document updates the visible branch after the workspace refreshes.

When editing causes the rendered preview to refresh, Axiom keeps the visible source block in place and uses the prior proportional position if that block no longer exists. This applies whether synchronized scrolling is on or off. Synchronized scrolling still controls whether deliberate scrolling in one pane moves the other pane.

Operation messages and progress now appear in the bottom status area. Workspace loading and current-buffer export show their progress and Cancel action there; persistent failure Details and Dismiss actions are there as well. The top area remains dedicated to commands.

Per-file outlines now follow heading structure: H2 sections nest below their H1, and H3 subsections nest below their H2. Headings without an earlier parent remain roots; skipped levels nest below the nearest earlier heading of a lower level. Expand/collapse heading branches with their arrows. New outlines start expanded; collapsed branches remain collapsed when the working buffer refreshes. Clicking the heading label still navigates to its current source and preview location without saving.

The packaged Guided walkthrough now includes an advanced Tools chapter covering all seven actions: Diagnostics, State Maintenance and verified backups, portable updates, per-user installation, trusted update offers, portable version switching and About. Use the step selector to jump directly to a Tools topic. Captions explain prerequisites, explicit choices and cancellation; screenshots show real review screens in isolated example state. Help remains the maintained reference for recovery/update details. Following a tutorial does not automatically install, download or change your documents.

## Workbench and guided walkthrough

The workbench has Workspace Files, the editor/Preview, and retained tool panels. **Workspace Files** is the left panel. Type in **Filter files** to narrow filenames and paths; matching files retain their folder hierarchy. Escape hides the filename filter while retaining its query. Choose Clear to remove it. The Discovery/search action opens full-text workspace search. `Ctrl+Shift+F` toggles that search panel.

Expand a folder to browse files. Expand a document to load its headings on demand. Folder arrows, document symbols and H1–H6 heading labels distinguish each row. Click a document once to open it, or select it and press Enter. Click a heading to open that document and move to its current source and preview position. Open documents use their unsaved buffer for the outline. If a heading changes, Axiom navigates only to a unique current match and reports an ambiguous/missing heading rather than using an obsolete position. Outlines in unopened files refresh when expanded again.

The right rail toggles **Search**, **Outline**, **Metadata**, **Relationships**, **Classification**, and **Backlinks**. Click the same button again or the panel's × to collapse it. Drag either panel divider to resize; widths, the selected view and visibility are remembered locally. The rail's **Reset workbench layout** button or the corresponding Command Palette action restores default widths and collapses secondary views. Opening Help replaces the secondary panel so the editor remains usable. Switching panels or resetting layout does not save, discard or modify working documents.

The toolbar uses icons for actions. Hover for descriptions and shortcuts; keyboard focus and accessible action names are retained. File, Edit, Format, Theme and Tools remain named menus. Reading and synchronized scrolling change their icons and tooltip descriptions to reflect their current states. Document tabs have rounded tops, a highlighted active tab, an asterisk for unsaved changes and a close button; a crowded tab row scrolls horizontally. Existing Save/Discard/Cancel protection still applies when closing a dirty tab.

**File > Workspaces and navigation** opens/refreshes workspaces and accesses recent documents or commands. **History and recovery** contains Git/history/checkpoint/deleted-document review. **Create and organize** contains document/folder creation and management. **Document** contains New, Save, Save As, Close, link navigation and structure. **Export current buffer** contains Word, HTML, text, PDF, EPUB and OpenDocument Text. **CP review and search** retains explicit reviewed CP workflows. Destructive operations, CP consent and detailed history review keep their dedicated review dialogs.

Press **F1**, then choose **Guided walkthrough** in the embedded Help panel, or search for **Open captioned app walkthrough** in the Command Palette. It opens the packaged `Walkthrough/index.html` in your browser. The offline walkthrough provides current instructions, Previous/Next controls, arrow keys and Home/End. Its steps complement the feature topics in Help. It requires no account, network connection, autoplay or audio. If your browser does not open, open that file directly from the extracted package.


## Manage documents and collections

The File menu adds named workspace document/folder creation, Rename, Move, Duplicate, Delete to Recycle Bin, Remembered Workspaces, and Make Axiom Documents. Select an explorer document, then choose a command. Destinations are workspace-relative and must not already exist. Moves retain identity and warn about relative references; enrolled duplicates get a new ID.

Remembered Workspaces opens another collection or forgets its saved record/pins without deleting files. Bulk enrollment starts with nothing selected; review paths/status, select eligible files, and Apply to explicitly save their IDs. Cancellation stops remaining work and retains completed files; inspect per-file results before retrying.

Press/release **Alt** or press **F10** to focus File, then Space/Enter to open its menu. **Alt+F**, **Alt+E**, and **Alt+M** open File, Edit, and Format directly. Move opens a folder picker: choose an existing folder inside this workspace; Axiom retains the filename and confirms the exact destination and reference warning.

The explorer selects one document. For bulk enrollment, choose **Enroll multiple files…** beside it or **File → Bulk Make Axiom Documents…**. Select multiple rows in that separate review window by clicking each row or pressing Space on a focused row; Shift selects a range. Watch the selected count, then explicitly Apply. Opening the window or selecting rows does not enroll files.

Files restored or added externally appear after the workspace watcher refreshes the explorer; your dirty editor stays intact. **F5** or **File → Refresh Workspace** requests a rescan. External restore does not recreate removed pins automatically; Axiom deleted-document recovery restores its recorded pin.


## Open and edit

Startup restores the active remembered workspace. Missing roots remain remembered for recovery.

1. Open **File…**, select **Open Workspace…**, and choose a document folder. Axiom adopts the folder as its active workspace and remembers workspace-owned state locally.
2. Expand folders in **Workspace Files** and click a `.md` or `.markdown` document, or choose **File… > New**. Folders appear before documents at each level.
3. Edit source on the left and read the live preview on the right.
4. Use **File… > Save** (`Ctrl+S`) for the current document, **File… > Save All** (`Ctrl+Shift+S`) for all modified open tabs, or **File… > Save As…** (no default shortcut) for another destination. Save All prompts for modified untitled documents and stops on cancellation or save failure, preserving remaining dirty buffers and the selected tab. Existing explicit custom shortcut bindings retain precedence; use Keyboard Shortcuts to review or reset any previously customized save bindings. Undo and Redo are under **Edit…**.

While you edit the current document, Axiom keeps the preview near the same proportional reading position after live updates. Opening or reloading another document starts that document at its own preview position. Wide tables scroll inside the preview, and large images scale down to the available width.

Use **Format…** to apply bold, italic, inline code, links, paragraph or heading styles, bulleted, numbered or task lists, and fenced code blocks to the current selection or caret. `Ctrl+B`, `Ctrl+I`, and `Ctrl+K` apply bold, italic, and link formatting while the source editor has focus. Each formatting action is one undo step and remains unsaved until you choose **Save**.

With the source editor focused, use **Ctrl+F** to find text or **Ctrl+H** for Find/Replace. Searches use the current editor buffer, including unsaved changes. Replace All changes that buffer; Undo restores the previous text. Your saved Markdown changes only when you save.

At the end of a non-empty list item, press **Enter** to continue its bullet, next visible number, or an unchecked task box. Press **Enter** on an empty list item to remove its marker and end the list. Press **Tab** or **Shift+Tab** to indent or outdent the current list item or selected list lines by one level. Outside list content, Tab keeps the editor's normal behavior.

To reference an existing image, first save the current Markdown document inside the active workspace, then choose **Image…**. If no workspace is open when a new document is first saved, Axiom adopts the containing folder as the workspace and refreshes Workspace Files. The image picker starts in that workspace. Select a PNG, JPEG, GIF, WebP, BMP, or SVG file that is also inside it. Axiom inserts a portable relative Markdown path; selected single-line text becomes the alt text, or a filename-derived placeholder is selected for you to replace. Preview resolves supported workspace-local images without rewriting that portable source reference. Saving the Markdown remains explicit. Axiom does not copy or manage the image file. If a selection is outside the workspace or otherwise unusable, Axiom leaves the Markdown unchanged and explains why.

Choose **System**, **Light**, or **Dark** appearance in Settings or the Command Palette to change the application and preview appearance. System follows the supported operating-system setting. Axiom remembers the choice across launches. Switching themes does not change the Markdown buffer, selection, caret, workspace, or undo history.

Opening a folder as a workspace does not copy, rewrite, save, or enroll any Markdown file. Selecting the same folder again reuses its Axiom workspace record. Startup restores the remembered workspace; Remembered Workspaces lets you switch collections.

The explorer shows Markdown files, their ancestors and genuinely empty folders. Non-Markdown files are omitted. Axiom does not follow linked directories; inaccessible or linked descendants are skipped with a warning while the rest of the workspace remains usable.

## Search the workspace

1. Open **Discovery** using Ctrl+Shift+F or its search icon.
2. Enter literal text and choose **Search**.
3. Review matches by relative file path, excerpt, and line number.
4. Double-click a result to open the document and select the matching text.

Search is case-insensitive and reads the current `.md` and `.markdown` files directly. It does not replace text, modify files, use regular expressions, or maintain a content index. Starting another search supersedes the prior one. If a result changed after the search, Axiom opens the document but does not select an incorrect location.

## Navigate by document outline

Select **Outline** in the right tool panel to see headings from the current editor buffer. The outline updates shortly after typing, including for unsaved changes. It supports normal `#` headings and setext headings, ignores heading-like text inside fenced code, and keeps duplicate heading names as separate entries.

Double-click an outline entry to move the editor caret and viewport to that heading. Outline navigation does not save or modify the document.

Outline and Preview share heading interpretation, including distinct fragment IDs for duplicate headings. Follow Markdown links and incoming references using the navigation topics below.

## Identify the running build

The right side of the bottom status bar shows the running Axiom product version and build provenance. Release builds use the publishing commit ID; local development builds explicitly show `dev`. This makes it possible to distinguish the executable being tested without opening or changing a document.

## Pin important documents

Open a Markdown document inside the active workspace and choose **Pin** in the toolbar, or right-click its file and choose **Pin or unpin selected workspace file**. The document appears in the **Pinned** group at the top of Workspace Files. Click a pin to open its normal document tab. Unpin through the same context action or the current document's toolbar action.

Pins are workspace-specific Axiom state. Pinning ordinary Markdown does not enroll or modify it. If a pinned file is moved or deleted outside Axiom, its pin remains visible and removable; opening it reports that the target is missing.

## Read the status areas

The footer shows operation/recovery information, then word count, line/column and running build. A dirty indicator means the buffer differs from its saved file. Document details opens the full workspace/path/identity/encoding/newline/recovery context. Success messages expire; operation failures remain visible with Details and Dismiss until resolved or explicitly dismissed. Dismissing a report does not resolve a conflict. Closing dirty work offers Save, Discard or Cancel.

## Axiom identity

Existing files do not become Axiom documents automatically. Select **Make Axiom Document** only when stable identity is useful. The new ID is an unsaved editor change until you save. If copied files contain the same ID, Axiom warns and offers a distinct ID for the copy.

## Structured Markdown and wiki navigation

**File → Document Structure…**, also available as **Browse document structure** in the palette, shows headings, sections, tasks, links, fenced/indented code and tables from the current buffer. Select a row and **Show source** to select its exact source range. Browsing never edits or saves Markdown. Logical node IDs are reconciled across ordinary edits; a changed or ambiguous node requires refreshing the browser. A section runs from its heading to the next heading of the same or higher level.

Optional wiki syntax supports `[[Document Name]]`, `[[Document Name|label]]`, `[[Document Name#Heading text|label]]`, `[[folder/document.md#heading-anchor]]`, and `[[id:document-guid]]`. A heading-only `[[#Heading text]]` uses the current document. Explicit paths are relative to the workspace root; names match filename stems, first heading titles and saved Axiom aliases. Name matching ignores case; duplicate matches require choosing a target. An exact anchor wins; otherwise unique heading text is matched without case sensitivity. Repeated heading text requires an exact anchor. Escape a literal pipe, closing bracket or backslash inside a wiki target/label with a backslash. Backtick/fenced code examples and escaped opening brackets remain literal.

Follow a wiki link from Preview or place the editor caret inside it and use **Ctrl+Enter / Follow Markdown Link**. The same navigation history and dirty-buffer protections apply. Wiki links appear in backlinks, including unsaved source overlays. Saved alias changes schedule an index refresh. While an index is incomplete, name/identity uniqueness remains unverified. Missing targets never create or enroll documents; resolve the missing target deliberately. Absolute paths, traversal, remote schemes and inaccessible/reparse targets are rejected. Wiki navigation preserves current unsaved target headings without saving either buffer.

The semantic foundation exposes structural snapshots and stores derived results in local state with document/content-hash/revision and provider/version provenance. It does not generate AI results in this version. Derived cache data never becomes Markdown or classification authority; later semantic features must verify matching provenance before using it.

## Recovery

### Deleted documents and recorded folder structure

**Delete Document to Recycle Bin** captures exact saved bytes, original workspace path, document identity, pin and a classification/relationship receipt before recycling. Capture failure prevents deletion. Protected recycle supports Markdown documents up to 20 MiB; larger files remain untouched. Resolve unsaved changes first: recovery captures the saved file, not discarded edits.

Choose **File → Recover Deleted Documents…** or **Recover deleted documents** in the command palette. Select a receipt to preview its captured source and status, then **Restore selected…**. Choose a workspace-relative Markdown path and confirm **Preserve identity** or **New identity**. Cancel keeps the capture and disk unchanged. Missing parent folders are recreated; recovery never overwrites an existing file. Choose another path when the original path is occupied. A preserved identity already present elsewhere requires resolving that conflict or explicitly choosing a new identity.

Preserving identity reconnects classifications and relationships still retained in current state. Newer metadata remains authoritative; the historical receipt never replaces it. A new identity does not inherit the original metadata or redirect original relationships. Missing relationship endpoints remain unresolved. The recorded pin is restored to the selected path.

Captures live in local Axiom state, separately from Windows Recycle Bin and Git history. They remain without automatic expiration; protect and back up that state if you need the captures. Recovery covers Axiom-observed document recycling and recorded parent paths, not arbitrary external deletion or recursive folder deletion. A corrupt receipt cannot be restored. Prepared/restoring receipts require restart reconciliation; cancelled/restored receipts remain visible and cannot be restored again. Parent folders created before an interrupted restore may remain empty; retry checks them safely.

Dirty text is protected locally without being written to the Markdown file. After a crash or forced reboot, Axiom can restore that text into the editor for review. Recovery never silently saves it.

If Undo returns the editor exactly to the saved file, Axiom clears the current session's obsolete recovery draft and returns the recovery status to **Recovery ready**.

## External changes

If another editor changes a clean open file, Axiom reloads it. If Axiom also has unsaved edits, a banner opens a side-by-side comparison. Choose **Use my edits**, **Reload disk version**, or **Decide later**. Axiom blocks Save while this conflict remains unresolved.


## Diagnostics, backups, and versions

Open Tools → About Axiom to identify the running build and its state location. Tools → Diagnostics provides Refresh and Copy for a bounded log tail. Review copied diagnostics before sharing because paths and exception details may appear.

Tools → State Maintenance shows database integrity/schema and record counts. Export verified backup chooses a new file and refuses an existing destination. Inspection and backup leave Markdown unchanged. Restore requires closing all Axiom processes and following the operational recovery procedure; live restore/reset is unavailable.

Tools → Install Portable Update first explains the operation, then asks you to select **payload-manifest.json** inside a newer published Axiom application package, beside **Axiom.App.exe**. Select the application package, rather than the Markdown workspace; extract ZIP downloads first. Axiom verifies it, displays its identity, and offers explicit installation into a new version folder. Restart is the button in the **Version ready** dialog, not a separate Tools command. It protects unsaved edits with Save/Discard/Cancel and carries forward the current state root. Later leaves this app running and displays the installed package directory. When ready, Tools → Switch Portable Version selects that directory's manifest and offers Restart. Switching also supports an older original package if it can read the current database schema. Keep old packages for rollback; do not replace running binaries manually.


## Open Markdown from Windows

The 1.3.0 development candidate opens .md and .markdown files passed from Windows in a remembered workspace when one contains the file. The active workspace wins when it contains the file; otherwise the most specific remembered root wins, then the most recently opened workspace. Forgotten workspaces are not rediscovered automatically. Files outside remembered workspaces open directly in a tab without creating a workspace or adding document identity.

An existing Axiom instance receives the file request. Workspace changes retain the existing Save/Discard/Cancel review for dirty documents; Cancel leaves your current workspace and edits intact. Opening an already-open document keeps its working buffer. For an external file request, Axiom clears an explorer text filter if needed to select and reveal the requested workspace file. Normal tab switching retains its existing filter behavior.

Choose Tools > Install for Current User from a verified package, then Register for Markdown Open with. After installation, Open Default Apps takes you to Windows Settings for that registration. Choose Axiom for .md and .markdown there. Registration offers Axiom as a handler; Windows keeps your existing default until you choose. Portable packages can still open a file using its full path argument. Removing the installed version removes its owned integration and keeps documents, state and backups.

## Work with document tabs

Opening a workspace file, pin, or search result keeps your other documents open. Selecting its tab preserves each document's edits, undo history, caret/selection and editor position. Opening the same file again selects its existing tab. Tabs with the same filename show relative paths; untitled documents have distinct numbers. An asterisk marks unsaved edits, and Conflict or Missing labels identify inactive files needing attention.

Press and release Ctrl+Tab to toggle between the two most recently active tabs. Hold Ctrl and press Tab repeatedly to cycle through all open tabs in recent-use order; Ctrl+Shift+Tab cycles backwards. The order stays fixed while Ctrl is held, and releasing Ctrl makes the selected tab most recent. Clicking a tab also updates recent-use order. Switching tabs retains unsaved text, Undo, caret and selection. Choose File → Close Tab, press Ctrl+W, or use the tab's × button to close it. Save/Discard/Cancel applies to that tab. Workspace switching, application close and version restart resolve all affected dirty tabs; cancellation or failed Save keeps the transition from proceeding. Save As refuses another tab's already-open path.

Recovery protects saved and untitled dirty tabs independently. Startup offers each protected draft; Later preserves that draft for another launch. Restoring a draft for an already-open clean file reuses that tab. No recovery choice silently writes Markdown.

After a normal close, startup restores saved-file tabs in their previous order and selects the active document. Axiom remembers caret/selection and editor/preview positions separately from Markdown. Each workspace remembers its own clean open set. Missing or unavailable files are skipped with feedback. Untitled work is recovered through protected drafts rather than treated as a saved file. Cancel or a failed Save stops a close or workspace/version transition; successful earlier saves remain saved and other buffers remain open.

### Follow Markdown links

Click a standard Markdown link in Preview, or place the editor caret inside an inline/reference link and use File → Follow Markdown Link or Ctrl+Enter. Axiom resolves same-workspace .md/.markdown targets, reuses an open tab and positions the editor at the requested heading. Open-buffer headings are used when that target has unsaved edits. Unsupported/external/outside-workspace links show feedback and do not launch content or switch workspaces. A missing heading offers Open document or Cancel; a missing document leaves the current tab selected.

Back/Forward buttons and Alt+Left/Alt+Right return to source caret locations while retaining dirty buffers and undo. Unavailable history locations are skipped with feedback; changing workspaces clears history. A changed preview link requires a refreshed preview. These actions never save or enroll a document. Native preview-click/fragment-scroll usability remains pending human acceptance.

### Incoming Markdown references

Select Backlinks in the right tool panel to inspect references to the active saved document. Results show source path, heading/text context, line and Saved or Unsaved provenance. Double-click a result or select it and press Enter to open the reference itself. A changed source is relocated only when the matching destination is unique; ambiguous or removed references require a rebuild.

Rebuild reads saved Markdown off the UI thread; Cancel retains previous results with a Stale indication. Complete means eligible files were processed, while Partial/Error lists skipped or failed paths. Failed source reads retain earlier references under the partial warning; deleted sources are removed on rebuild. Saved file/folder changes schedule a refreshed index. Dirty source tabs replace their saved results with an Unsaved overlay, including an empty overlay when links are removed; Discard restores saved results and Save schedules a saved-file rebuild. Untitled buffers cannot supply relative-path references. Unverified overlay targets are checked when followed; open-target headings are checked against their live buffer. Indexing and navigation never enroll ordinary Markdown.

## Document metadata and classification

Open **Metadata** in the right tool panel to inspect the active document's ID, workspace path, and saved identity status. Tags and aliases are entered one per line; Type is optional free text. **Apply** saves these values locally without saving Markdown. **Cancel** discards the panel edits. Leaving the document with pending panel edits offers Apply, Discard, or Cancel. Metadata editing requires a saved unique canonical document ID; use **Make Axiom Document** and deliberate Save for ordinary files, then Refresh saved identities. Untitled, missing, ambiguous, and unsaved identity changes remain read-only.

If the fields are greyed out, read the explanation above them. For an ordinary document, open it from an active workspace, click **Make Axiom document** in Metadata, then **Save document**. Enrollment first adds an unsaved ID marker to the editor; Save deliberately writes it to Markdown. **Refresh saved identities** checks the saved file again. Copied IDs and incomplete discovery show their own reasons and require resolution before editing. Merely opening Metadata never enrolls or saves anything.

For a first example, enter `Project` and `Decision` on separate Tags lines, `Note` as Type, and `Launch plan` as an Alias, then click **Apply**. Tags group documents, Type describes one document, and Aliases are alternate names for finding it; they do not rename files or change links. Open **Classify**, enter `Project` in Tags or `Launch plan` in Filename or alias, and click **Filter**. All entered tags must match; Type matches exactly, and filename/alias uses a literal substring. **Clear** removes the filters. These values are local Axiom metadata; explicit export/import transfers them to another state.

The **Classify** tab filters by every entered tag (one per line), optional exact type, and filename or alias text. Matching is case-insensitive; aliases are alternate names, not filenames or Markdown-link targets. Clear removes filters. Refresh rereads saved identities and reports partial discovery. Double click or Enter on a result opens its existing tab.

**Export metadata** reviews unique saved identities and writes a selected JSON file. **Import metadata** previews every record. All changes default to Skip; select each replacement you want and Apply selected replacements. Missing/ambiguous identities cannot be selected. Files or metadata changed since review require a refreshed preview. Imports replace selected tags/type/aliases atomically, never save/enroll Markdown, and never erase records absent from the transfer file. Local metadata survives rename, missing/recycled documents and forgotten workspaces; a duplicated document with a new ID begins empty. Keep an explicit JSON export if you need these local values in another Axiom state.


## Explicit relationships

Open **Relations** in the right tool panel for incoming and outgoing connections for the current saved Axiom document. **Add** creates a directed connection from this document to another unique saved Axiom document in the workspace. Choose a target, keep or change the neutral **Related** kind, enter an optional note, and Apply. Self connections and duplicate direction/kind pairs are rejected. Reverse connections are independent.

Select an outgoing relationship to **Edit** or **Remove** it from its source document. The editor makes an explicit proposal; Cancel leaves it unchanged. Removal confirms your intent. Double click or Enter follows the other endpoint in its existing tab and retains dirty buffers; Back/Forward return to prior locations. A missing or ambiguous endpoint stays visible by ID and does not guess a path. Refresh identities after a file returns or a copied ID is resolved. Relations remain local metadata and do not modify Markdown links or source text.

Metadata export now writes version 2 with classifications and relationships. Unresolved relationship IDs remain in the export when one endpoint belongs to the active unique collection. Import still accepts version 1 classifications. Its review defaults to Skip for every classification/relationship; select each intended replacement. Missing/ambiguous relationship endpoints cannot be applied, and stale files or revisions require refresh. A selected mixed batch commits atomically; failure leaves every value unchanged. Keep the explicit JSON export for transfer to another Axiom state.


## Workspace loading, reading mode, and Help

**Open Workspace** selects a physical folder containing Markdown. A loading indicator appears while discovery, identity inspection, and metadata discovery run in the background. **Cancel loading** stops discovery and retains the previous workspace and every open buffer. After discovery succeeds, existing Save/Discard/Cancel and pending-metadata safeguards resolve work before adoption. Failures retain the prior workspace. Linked or inaccessible paths are reported separately; partial metadata discovery disables identity-bound changes. Remembered workspaces remain available through File commands.

Click a Markdown leaf once in **Workspace Files** to open/reuse its tab. Folders still select/expand; right click and keyboard navigation remain separate. Enter opens a selected leaf. Double clicking reuses the same tab. Use **Enroll multiple files** for the separate bulk review workflow.

Click the reading-mode icon or press **Ctrl+Shift+R** to hide the editor and let Preview fill the document area. **Show editor** or the same shortcut restores editing. Tabs, navigation, metadata, conflict banners and dirty indicators remain visible. Toggling does not save, discard, alter identity, or reset undo/caret/selection/scroll; hidden buffers remain recoverable. The preference persists locally, including after clean startup. Recovered unsaved tabs retain their dirty indicators in reading mode.

Click **Help** or press **F1** for a bundled offline copy of this maintained User Guide. The topic selector jumps among headings; context actions from the sidebar suggest relevant workspace, metadata/classification, relationships or navigation guidance. Help does not require enrollment or a network connection and does not modify documents. The bundled copy is generated by `scripts/Sync-OfflineHelp.ps1` from this guide and carries a source SHA-256; edit this guide, then regenerate it. Use the topic selector and feature links to navigate the bundled guide. Main menu → Help offers all feature topics plus direct CP connection and semantic-test topics.

With **Relations** selected, F1 opens **Explicit relationships** even when focus is on its header, a button or an empty list. With **Metadata** or **Classify** selected, F1 opens **Document metadata and classification**. The Help topic selector shows the selected heading so you can confirm the context or choose another topic.

## Export to Word and HTML

Use **File → Export current buffer to Word** or **Export current buffer to HTML**. Export uses the active document as it appears now, including unsaved edits. It does not save the Markdown, change enrollment, or discard your work. Choose an output filename ending in `.docx` or `.html`; replacement requires confirmation. Cancel export during conversion/writing to retain any existing output.

Both formats support headings, paragraphs, emphasis, lists, static task checkboxes, links, tables, quotes and code. HTML is a standalone offline file; Word export requires no Word installation. Supported PNG/JPEG/GIF/BMP images are embedded relative to a saved document path, up to 20 MiB each. Missing/unsupported images or images in untitled documents retain alt text and produce warnings. Remote images are not downloaded. Raw HTML is displayed as text; unsafe links are omitted. Axiom identity markers and separate classification/relationships are not exported. Links to Markdown targets are not recursively exported or rewritten. Word page layout may differ from Preview and requires human review; export is not a lossless import/export editing workflow.

Only the active document is exported. Metadata JSON transfer remains a separate workflow. Export completion shows the destination; errors and warnings explain incomplete conversion. Use **Cancel export** to stop a pending write; existing output is retained until successful commit. Choose an output location outside Axiom state and open/source documents.

## EPUB and OpenDocument Text export

Choose **File → Export current buffer to EPUB** (`.epub`) for reading or **Export current buffer to OpenDocument Text** (`.odt`) for editable interchange. Both capture the active unsaved buffer and use the existing overwrite confirmation, cancellation and protected-output rules. They do not save Markdown, enroll documents or change recovery state.

One Markdown document becomes one EPUB reading chapter, with a contents entry for each heading. ODT retains headings, emphasis, lists and task symbols, tables, quotes, code and bookmarks. Both embed supported local images relative to a saved source path and deduplicate identical image bytes. EPUB supports PNG/JPEG/GIF; BMP retains alt text with a warning. ODT also embeds BMP. Resources are bounded to 20 MiB per image, 64 MiB total and 1,024 unique images. Remote images are never fetched; untitled/missing images retain alt text and warnings.

Safe web/mail links and valid current-document heading links are retained. Links to other local documents and missing heading targets retain their label with a warning; linked documents are not bundled. Raw active HTML, Axiom identity markers and separate metadata/relationships are excluded. EPUB reflows with the reader's font and viewport; ODT page layout and fonts depend on the office application. Format validation does not establish reader layout acceptance. Review outputs in your intended readers after implementation is complete.

## Searchable Help while working

**Help** opens a side panel in the main window. Keep it open while editing, selecting tabs, reading or using metadata and relations. Search matches literal text in headings and the body, without regard to case. Select a matching passage to jump to its topic and highlight matching text where it appears in the rendered guidance. Empty and no-match searches report their state. Browse topics at any time; ordinary focus changes do not retarget a manually chosen topic. **F1** or explicit contextual Help chooses the relevant topic. **Close Help** hides the panel; reopening retains its topic. Use **−/+** to narrow/widen it, or close it to recover the full document area.

Help is generated from this maintained User Guide and stays offline. Product changes must update this guide and regenerate bundled Help; builds and portable publishing check for stale Help when the maintained source is available. Native keyboard focus, themes, DPI and narrow-window usability remain acceptance checks for this development build.

## Settings and editor preferences

Choose **Settings** to configure font family, font size (8–40), wrapping, tab width (1–8 columns) and Light/Dark/System appearance. **Apply** persists the choices; **Cancel** preserves earlier settings. **Reset editor defaults** prepares the original editor choices in the dialog; press Apply to save them. Unknown fonts fall back to Consolas/monospace with status feedback. Settings affect every editor tab and survive restart without changing source text, unsaved work, selection, undo or recovery. Theme uses the existing application appearance preference. Keyboard Shortcuts lets you review, override and reset command bindings.

## Recent documents

Use **File → Recent Documents** to reopen one of the latest 20 successfully opened documents, with its path shown. Open reuses an existing tab and uses the existing safe workspace switch if needed. Missing files or workspaces give actionable feedback. Cancelled/failed opens do not enter history. **Remove selected** and **Clear history** forget only history; they never delete files, pins, metadata or protected drafts. Remembered Workspaces remains the single workspace registry.

### Icon and selectable menus

Ben selected the teal architectural A icon and both menu looks. The application window and Windows executable use the A icon. In Settings, choose Menu style: Quiet (flat groups) or Rounded (rounded groups), then Apply. The choice persists across restarts in the existing editor preferences setting; older preferences default to Quiet. Cancel leaves the selection unchanged. Reset editor defaults retains the menu choice. Both styles preserve File/Edit/Format commands, shortcuts and working buffers. Native visual review at different themes and display scales remains a human acceptance check.

## Plain-text export

Use File → Export current buffer to plain text to write a UTF-8 `.txt` file from the active buffer, including unsaved edits. The same overwrite confirmation, cancellation and protected-destination rules apply as for Word and HTML. Markdown files and recovery state are unchanged. Headings and paragraphs become readable text; lists retain nesting and numbering, tasks retain checkbox symbols, quotes retain a `>` prefix, and code retains indentation. Links include their destination when different from the label. Images retain alt text without embedded bytes. Tables use ` | ` separators and warn that column alignment is approximate. PDF export is available in the current application.

## PDF export

Use File → Export current buffer to PDF, choose Letter or A4 portrait, and select a `.pdf` output file. Export captures the active buffer when invoked, including unsaved edits, and writes directly without a printer dialog, Office or an online conversion service. Output uses neutral light styling, 18 mm margins and page numbers. Headings, emphasis, nested lists/tasks, quotes, code, safe hyperlinks, tables and supported local images are retained. Table headers repeat; very tall rows can continue across pages and produce a review warning. Long code and URLs wrap; images fit printable width and height. Missing/unsupported images retain alt text and warnings. No remote images or executable HTML are loaded. Unicode is searchable/selectable using installed Windows fonts and browser fallback; unusual scripts require a suitable installed font.

The Windows export renderer requires the same WebView2 Runtime as Preview. Export is limited to 4 million source characters, 64 million HTML characters including embedded images, 20 MiB per image and 128 MiB output. Native browser PDF rendering times out after 60 seconds. Cancel or failure preserves an existing destination and the active document; only successful output commits replace it. Cancellation closes the isolated renderer; it cannot interrupt a synchronous Markdown parse already executing. Keep your Markdown as the editable source.

Linux uses the bundled offline graphics renderer rather than a browser printer. It supports Letter/A4, page numbers, headings/emphasis, lists/tasks, quotes, wrapped code, safe web/mail links, local images and tables with repeated headers and split tall rows. Its margins are 48 points. Limits are one million source characters, 2,048 pages, 32 table columns, 4,096 distinct styled characters, 1,024 images, 20 MiB per image, 16 million decoded pixels per image, 64 MiB total image bytes and 128 MiB output. Images inside table cells retain alt text; move them outside the table to embed them. Complex scripts, emoji and copied Unicode depend on installed fonts. For example, the Windows verification font rendered Japanese correctly but copied some characters as compatibility radicals. Review the warnings and actual PDF before sharing. Native Linux execution and macOS browser PDF geometry remain unverified.

## Templates and named creation

File → New Workspace Document offers Blank or Choose template. Keep reusable `.md` or `.markdown` templates in any local folder you own; the template picker selects a file without changing it. Templates can contain `{{title}}` (the new filename without its extension) and `{{date}}` (local creation date as `yyyy-MM-dd`). Other tokens remain literal. Templates do not run scripts or fetch online content. A canonical first-line Axiom identity is removed from the expanded text, so the template's identity is not copied. New documents remain ordinary Markdown until explicitly enrolled.

Choose an existing folder inside the workspace and enter the Markdown filename. Review all expanded Markdown in the preview before choosing Create document. Cancel closes the workflow without creating a file. Existing filenames, invalid names, paths outside the workspace and linked destinations are rejected. Creation opens a new tab and retains existing dirty tabs. Templates are limited to 4 MiB; relative links and images remain relative to the new document and may need adjustment.

## Drop files and import assets

Drop local Markdown files onto Axiom to open them using the existing tab workflow. Opening keeps the files at their original locations and reuses already-open tabs with unsaved edits. Unsupported files are reported and are not copied. To deliberately copy a Markdown file instead, use File → Copy Markdown into Workspace, choose an existing destination folder and a filename, then confirm the source and destination. Ordinary Markdown copies remain ordinary; an enrolled copy receives a fresh identity. Relative references are not rewritten. Copies use the disk content captured for review, including when a source file is open with unsaved edits.

Use File → Import Image into Workspace, or drop an image, to copy a local PNG, JPEG, GIF, WebP, BMP or SVG into an existing workspace asset folder you select. Save the active document inside the workspace first. Choose a filename retaining the original extension and confirm the copy. Axiom inserts an escaped relative image reference at the caret, using selected single-line text as alt text where possible. Existing image filenames are rejected; choose another name rather than overwrite. Imported bytes are retained exactly and the original stays untouched. Reference insertion is an editor Undo action; undoing it retains the imported image file. If reference insertion fails (for example the caret touches the identity line), the dialog reports the retained image location so you can insert it later. Markdown imports are limited to 4 MiB and images to 20 MiB. Files are processed separately; an unsuccessful file does not roll back earlier successful imports. Copy failures clean up temporary output; a completed copy with a state failure requires restart reconciliation.

## Preview navigation and synchronized scrolling

Preview at caret moves the rendered preview to the editor caret's Markdown block. Each rendered top-level block has a Source link that shows the editor, moves the caret to that block's source and focuses the editor. These links are available by keyboard. They use the current rendered document; a stale link after an edit is ignored until Preview refreshes. Markdown links continue to use the existing safe navigation workflow. Document HTML/scripts remain disabled, and synchronization uses application-owned browser code.

Scroll sync starts off on each launch. Toggle it on to synchronize scrolling between editor and preview, using structural blocks rather than whole-document height. Scrolling the preview changes the editor viewport without moving the caret or selection. Scroll sync is suspended in reading mode, during document transitions and while Preview is stale; toggle off whenever you want independent scrolling. Mapping within a large paragraph, table or code block is approximate because source and rendered heights differ. Preview position is retained through edits and tab switches using a unique unchanged block prefix where available, with proportional scroll fallback when the anchor changed or is ambiguous. Navigation, scrolling and reading mode do not save or modify source text.

## Document statistics and operation feedback

The bottom bar shows source-word count and one-based line/column at the caret. Words are contiguous Unicode letter/digit runs with internal apostrophes; code, link destinations and alt text count, while the canonical document identity does not. This is a source count, not a language-aware reading count. Counts refresh after a short typing pause. The format display reports the encoding observed during file reading and the current buffer's CRLF, LF, CR, mixed or absent newlines. Without a BOM, the reader assumes UTF-8; recovered buffers can show encoding unobserved. Save retains the established policy of UTF-8 without BOM. The display does not offer arbitrary encoding conversion.

Success/information messages are nonblocking and expire after eight seconds. A reported operation failure remains visible, with Details and Dismiss controls, even if later informational messages occur. Details displays the full message; dismissing it does not resolve the underlying failure or any existing conflict/reconciliation gate. Dirty/save/recovery state remains in the bottom bar.


## Command palette

Use File → Command Palette or Ctrl+Shift+P to search the available commands. Type a title or command ID, use Down/arrow keys to select a match, and Enter or Run to invoke it. Unavailable commands show a reason and cannot run. Escape or Cancel closes the palette and restores focus. The catalog includes editing, formatting, workspace and document management, templates/import, metadata/relationships, history/Git, appearance, diagnostics and Windows installation commands. Commands that use a panel selection require that selection first.  The palette displays current configured bindings.

## Keyboard shortcut settings

Use File → Keyboard Shortcuts to edit bindings for currently registered commands. Apply validates the whole set and saves it in local application state; changes survive restart. Blank removes an application binding. Cancel discards staged edits. Reset defaults stages the original bindings; press Apply to save the reset. Conflicting or reserved gestures show a validation message and keep the dialog open. The palette displays effective bindings; its Ctrl+Shift+P access remains reserved.

Use Ctrl/Alt/Shift plus a letter, digit or F1–F12. Plain typing keys, Ctrl+Alt (AltGr), standard clipboard/selection, formatting/navigation/menu access, and native editor Undo/Redo are protected. Undo/Redo remain read-only. Unknown retired command IDs are ignored during loading; unsupported or invalid saved configurations fall back to defaults and report an error without overwriting stored data. Save still follows existing confirmation/recovery rules. The registered command catalog is configurable, while protected native editor and navigation gestures retain their existing behavior.
## Document history, comparison and restore

Git is required only for durable document history. Normal editing, Undo and recovery work without it. Axiom uses local Git history as the revision authority; recovery drafts remain independent. Save never creates a Git commit automatically.

For an existing Git repository, save/open a document and choose File → Document History, or search for Browse document history in the palette. The window lists up to 200 reachable document revisions with commit date, hash, subject and historical path. Git detects committed renames; this does not promise history for uncommitted edits, ambiguous renames or edits made before checkpointing. Select a revision to read its exact Markdown source. Compare it with the captured current buffer, saved buffer, external-change snapshot (when present), or another selected revision. The read-only line viewer shows previous/current line numbers, added/removed/unchanged markers and line-ending differences. Comparison is bounded to 4 Mi characters and 100,000 lines per buffer, and two million comparison cells after shared prefix/suffix trimming. Revisions/checkpoints are limited to 4 MiB. An oversized or unavailable revision reports an error without changing source.

Restore into buffer asks for confirmation, then resolves existing unsaved work with Save, Discard or Cancel. Cancel retains the current buffer and its recovery. External-change conflicts must be resolved first. Axiom preserves the current enrollment identity: a different/missing historical identity is replaced by the current document identity, and an ordinary Markdown document stays ordinary. The restored content is written to recovery before changing the editor; a failed recovery write prevents restore. Restore is undoable and changes only the buffer. Review it and Save explicitly to replace the file; existing conflict/save protections still apply.

If the workspace has no repository, File → Set Up Git History offers explicit local Git initialization. Confirming creates only .git; it does not commit documents or configure a remote. A workspace already covered by Git is not reinitialized. File → Checkpoint Saved Document requires a saved, conflict-free document and reviews its path, repository, byte count and SHA-256 before confirmation. A checkpoint records only those reviewed bytes; changed bytes after review are rejected. It uses a temporary index and a local refs/axiom/document-history reference, leaving the user branch, HEAD, real index and unrelated files untouched. Cancellation creates no visible checkpoint before the final atomic publication; publication finishes once started.

Axiom checkpoint references are local and path-specific. Ordinary Git pushes/clones do not automatically transfer these references. Back up the repository if these revisions matter. Existing committed Git rename history is followed, while a path-specific Axiom checkpoint reference is not automatically moved to another path by a filesystem rename. Workspace Git integration is available in the managed Changes tab; broader history transfer remains external Git work. History source can also be unavailable after deletion or repository changes; choose another readable revision. None of these actions enrolls an ordinary document.

## Workspace Git

File → Workspace Git, or the command palette, shows the local repository, branch and changed paths. Select a document to compare HEAD with its saved file or staged content. The Git view reads saved files; dirty drafts and externally conflicted open documents must be resolved before staging or committing. No operation silently saves a draft.

Stage selected and Unstage selected require explicit confirmation and act only on selected regular Markdown files inside the active workspace. Git's configured line-ending normalization applies to staged content; saved files remain unchanged. Inspect Staged diff to see the bytes that will be committed. Deleted documents can be staged deliberately. Other file types, paths outside the active workspace, coordinated renames/copies, custom/LFS filters, submodules, sparse checkouts, detached HEAD and unresolved Git operations require external Git. Git is optional for editing.

Commit selected staged asks for a message, then reviews the branch, paths and message. It commits only those selected staged entries, using your configured Git author identity. Unrelated staging, working files and dirty buffers remain unchanged. Managed Changes commits are local, unsigned and run without repository hooks; use the terminal for signing or hook-governed workflows. Managed Changes never pushes, pulls, changes branches or resets a working tree; the embedded terminal runs the commands you enter. Concurrent HEAD/index/source changes reject a stale managed review. Cancel stops before publication; a commit already published remains committed. Diff/source reads are limited to 4 MiB per document.
## Per-user installation and trusted updates

From a verified published Windows package, Tools → Install for Current User creates a verified state backup, installs into the existing local Axiom Versions folder, and registers Windows uninstall for the current user. Start menu shortcut and Markdown Open with registration are separate explicit choices; your default editor remains yours. Retained copies of the exact payload can be registered without duplicating that version. Existing versions and all documents, dirty buffers and recovery state remain. Installation does not restart this running app; use the existing Switch Portable Version workflow when ready.

Close the installed version before using Windows Installed apps to uninstall. Its helper verifies regular owned payload paths and hashes before deleting that version and its current registration/shortcut/Open with entry. Modified or linked payloads stop uninstall before removal. State, drafts, documents, backups, helper receipts and other retained versions remain; remove state only as a separate deliberate maintenance operation. Uninstalling an older retained payload does not remove a newer registration.

Tools → Check Trusted Update contacts only the HTTPS offer URL you explicitly provide. Enter the offer JSON SHA-256 obtained through an independently trusted publisher channel, then review version, channel, schema and release notes before choosing Download. No feed is configured and no background update checks run. Redirects, embedded URL credentials, escaping/linked/duplicate ZIP paths, oversized downloads and contradictory compiled payload identities are rejected. Bounds are 256 KiB for offers, 512 MiB downloaded ZIP, 1 GiB expanded payload and 10,000 entries. Cancel leaves nothing installed; isolated partial/failed downloads remain for diagnosis. A hash supplied by the same untrusted download source does not establish publisher trust.

A verified download is offered to the existing Install Portable Update review, schema and restart workflow by choosing its payload-manifest.json. That workflow now creates a verified state backup before switching versions. Older incompatible schemas are rejected. About includes source/build/schema/channel, package release notes and retained version location; legacy manifests show unspecified metadata. Public distribution, code signing, physical input and human release acceptance remain pending; package integrity alone is not publisher authentication.
### Shared commands

The palette includes commands for formatting, current-tab closing, back/forward/link navigation, workspace refresh/open, selected rename/move/duplicate/recycle/folder creation, all supported export formats, editor settings/shortcuts, diagnostics/state/About and scroll synchronization. These menu paths use the same command implementation; existing fixed editing/navigation shortcuts remain reserved. Destructive palette actions still require their normal confirmations; Cancel preserves saved and dirty source. Formatting retains the canonical identity marker and one-action Undo. Export reads a captured unsaved buffer and retains atomic output safeguards. Shortcut Apply validates the whole binding set; Cancel after Reset retains the last saved configuration.
### Command workflows and cancellation

Workspace catalog/recent documents, template creation, Markdown and image import, bulk enrollment, buffer enrollment, pinning, image references, appearance, metadata and relationship actions, search/indexing, selected-result navigation, portable install/switch, conflict review, tab cycling and status review now share the command catalog. Actions retain their original confirmations, identity checks and persistence boundaries. Make Axiom Document changes the buffer only; Save writes the identity. Ctrl+Tab and Ctrl+Shift+Tab retain working buffers when switching tabs. A tab's close button targets that tab, including an inactive tab.

Export Cancel, workspace-loading Cancel and reference-index Cancel can interrupt an active command. They request cancellation; a successfully committed output is retained. Cancellation does not resolve an external conflict or remove existing recovery. Local fields and buttons inside an already-open review dialog remain part of that workflow. Pointer selection, native text input, automatic refresh, startup/recovery and browser link gestures retain their dedicated event routing and existing guards.

## CP connection and reviewed analysis

Configured Windows builds connect and initialize local CP automatically. Choose **File → CP review and search → Analyze this document** to analyze the active text, including unsaved edits, immediately. Opening the action panel alone for **Suggest edits** sends nothing: describe the requested change and choose Suggest edits. Preview the diff, then explicitly Apply to the unsaved buffer; Undo reverses the application and Save remains your choice. A changed source or changed document identity prevents applying an old proposal. Generated facts and edits require your judgment.

Use **Advanced connection** only for a different service or troubleshooting. Its **Connect local CP automatically** button starts/checks the configured API and reads the existing Windows-protected credential internally. Normal Analyze/Search use does not require the external review launcher, a port number, clipboard copy or password entry. Setup/model progress, cancellation and retry are shown beside the action. No document is sent during connection checks; an explicit Analyze/Suggest/Search action submits only its selected snapshots. Opening another document never submits it automatically.

File → CP connection and reviewed insights opens the optional Development connection screen. Choose an HTTPS CP endpoint, or an explicitly selected loopback HTTP Development endpoint. Enter the dedicated document-analysis credential: it stays in session memory, clears from the entry box after connection, and is discarded on Disconnect or application exit. This is a separate credential from CP health/Admin access. Capability checks send no document text. The server must include the Development document-analysis routes and be enabled with its dedicated credential hash; the normal CP API endpoint alone is insufficient. A 404 means the analysis route is missing, 401 means the dedicated credential was rejected, and 503 can mean analysis is disabled. See Local CP controls and Semantic search: step-by-step test in these Help topics for connection and review instructions. Unavailable CP leaves offline editing, literal search, recovery and exports available.

Review the exact current buffer, including any unsaved text and document marker, before choosing Submit reviewed snapshot for insights. The question and that snapshot are sent explicitly; no automatic workspace upload, source save or document edits occur. Cancel request stops waiting and propagates cancellation to CP. Derived text appears separately with source-hash freshness, time and provider/model provenance. Model-generated claims and citations require review; a returned model identifier does not attest an immutable model revision.

File → Reviewed semantic search and cross-document analysis lists up to 16 open buffers: the current document and open documents inside the active workspace. Select the documents yourself and review the text. Add saved workspace documents for review lets you explicitly choose closed Markdown files anywhere inside the active workspace, including subfolders. Files are captured locally and added unchecked; selection does not submit them. Open files must use their reviewed buffers. Each saved file is limited to 1 MiB/200,000 characters, with at most 16 files per picker selection and 64 saved candidates per review session. Select 1–16 documents totaling at most 200,000 characters for a request. Saved source hashes are re-read before requests and navigation; changed or newly opened sources require a fresh review. Build reviewed semantic index submits only selected source sections; the active index remains in this screen's session memory. Optionally select Reuse/store derived vectors in local Axiom state to persist vectors only (no source text or credential), keyed by endpoint, model digest, source hash and source range. Unchanged sections are reused incrementally; an unavailable digest disables persistent reuse. Type and Tag filters use the captured saved metadata locally and are not sent to CP. A cancelled/failed rebuild never replaces a completed index with a partial one. Semantic search sends your explicit query and ranks matching source sections; Related excludes the first selected source document and proposes matches without creating stored relationships. Similarity scores are rankings, not confidence probabilities. Open selected source section checks captured hashes before selecting its source range.

Analyze reviewed documents sends the selected snapshots and question for separate cross-document output. Derived output includes a source-hash manifest, provider/model/provenance and creation time so explicit copying retains the analysis context. CP is asked to cite source lines using [[referenceKey|Lfirst-Llast]]. Open selected cited source range navigates only a reference that resolves to a reviewed source and valid line range with a current hash. Invalid/missing citations remain unverified; a valid source range does not prove that the model claim is true. Source changes/conflicts require closing and reopening the review screen; changes to the selected source set require rebuilding the semantic index. Clear derived index/results discards them. Selecting derived text and copying it is explicit. Clear all persisted semantic results removes the derived caches without changing Markdown, metadata or recovery. Ollama catalog digests are checked before/after embedding execution and on queries; unknown digests stay explicitly unverified. Manually entered credentials remain in session memory; configured local Windows helpers retain their credential outside Axiom. Review model relevance and grounding yourself. The initial Development endpoint supports existing local Ollama infrastructure; unsupported/simulated providers are reported rather than silently substituted.

## Read-only browser and phone companion

An operator can enable the read-only companion in the existing CP Development API and explicitly publish selected original saved Markdown workspace roots. Open `/companion` on that server, check its displayed origin, and connect with the separate companion credential. This feature defaults disabled; remote access requires HTTPS, and loopback HTTP is available for local testing. The credential stays in browser session memory and is cleared from the input after connection. Unsaved desktop buffers are not published.

Choose a shared workspace, browse its folder tree, or run literal search. Search is bounded and may require Search next batch; restart it after file inventory changes. Reading shows the saved source hash and read time. Refresh current document checks for changes. Show source exposes original Markdown, including its canonical identity marker; preview hides that marker and omits active URLs/images/raw HTML. Task states appear as read-only text. The companion has no editing, saving, creation, deletion or metadata mutation controls.

If the operator explicitly publishes the workspace's Axiom schema-8 or schema-9 metadata database using MetadataDatabasePath, the document can show saved Type, Tags, Aliases and incoming/outgoing relationship IDs/kinds. Context is read-only and requires a unique stored identity/path match. Other documents' paths and relationship notes are not exposed; a related ID never grants access to an unpublished document. Metadata has its own revision/read timestamp and is not claimed to be atomic with the Markdown snapshot. Unsupported, unavailable or ambiguous metadata leaves reading available.

Disconnect and clear or closing the page discards the session credential and displayed content. No editable synchronized copy or offline cache is created. An unavailable service leaves the last snapshot visibly stale until refreshed. Actual phone, HTTPS hosting, accessibility and human acceptance remain pending; desktop browser tests at phone dimensions do not establish physical-device acceptance.

## Platform state compatibility

The current application uses state schema 9. Startup retains a verified backup before migration and preserves workspace IDs, pins, identities, settings and unsaved recovery drafts. Older schema-8 applications cannot open upgraded state; keep separate application state roots when comparing versions.

Windows keeps its existing case-insensitive path identity. macOS/Linux conservatively retain exact path spelling so distinct case-sensitive filenames are not merged. Use the workspace folder picker and the filesystem's actual spelling. The state records its path policy and refuses to reinterpret state created under an incompatible policy. Use separate state roots when moving between Windows and macOS/Linux; adopt the original Markdown collection explicitly rather than copying active application state between policies.

On macOS/Linux, opening an existing file also checks its device/inode through `/usr/bin/stat`, reusing an already open tab for the same physical file. Save refuses to overwrite a physical file owned by another tab; choose another destination or close the other tab deliberately. These checks preserve unsaved buffers and do not change document IDs. If the system utility is missing or cannot identify the file, the operation reports the failure. Case-insensitive Unix volumes, native utility execution, launch and platform parity still require native verification. Cross-published previews are not accepted supported-platform releases.

## Save shortcuts and pinned-file labels

The default save gestures are: Ctrl+S saves the active document, Ctrl+Shift+S saves all modified open documents, and Save As remains in the menu without a default shortcut. Explicit user shortcut overrides remain user settings. Save All retains cancellation, conflict checks and remaining dirty buffers.

Pinned rows show the filename; hover for the workspace-relative path, including when two pinned documents share a filename. Git's changed-document list shows only supported Markdown (.md and .markdown); use external Git for other files. Repository-wide safety checks still apply. Spelling underlines clear and refresh when changing tabs so one document's offsets cannot appear on another.


## Empty folders, edge scrolling and ordered-item deletion

Workspace Files keeps genuinely empty folders and their ancestors visible after refresh or reopening. Populated branches containing only other file types stay hidden; Git administration folders are excluded. Creating a folder does not create placeholder Markdown files.

When dragging a workspace item near the top or bottom of Workspace Files, the list scrolls while you hold at the edge. Moving away, leaving, dropping or cancelling stops scrolling. Drops still use the normal move confirmation and dirty-document checks.

Selecting complete ordered-list item lines and pressing Delete or Backspace removes them and renumbers following siblings in that list. The original starting number, nested numbering, line endings and unrelated lists remain. One Undo restores the deletion and renumbering. Partial-text deletion and edits inside fenced code use ordinary editor behavior; opening or previewing a document never renumbers its source.

Compact caption buttons use centered glyphs, theme-aware hover and visible keyboard focus. The close button has a red hover treatment. Standard Windows title-bar fallback remains available in Settings.

## Daily authoring: tab order, column selection and document details

Drag a document tab onto another tab to change their order. Move tab left/right in the tab context menu or command palette provides the same action without dragging. Axiom remembers saved-file tab order in workspace state; it does not save dirty Markdown when reordering. Caret, selection, Undo history and protected recovery remain attached to the original tab. Untitled drafts continue to use recovery rather than saved-file session locations.

Hold Shift+Alt and use the arrow keys to select a rectangular region of source text. Typing replaces the selected columns across rows; Undo restores the edit as one action. In the selection-retention follow-up candidate, switching between open tabs also retains rectangular selections (including zero-width multi-row insertion), selection direction and the caret. This applies within the running session; restart position restoration retains its existing offset-based behavior. Rectangles touching the canonical document identity line reject typing, deletion, cut and paste. Keep that marker intact.

New and reviewed blank/template creation assign one Axiom identity to the new document. Templates do not donate their identity. The identity remains in source Markdown and survives recovery and Save; Preview hides the marker. Opening existing ordinary Markdown continues to leave enrollment under your control. Tabs show a diamond for Axiom documents and a document glyph for ordinary Markdown; their tooltip includes the identity/path/recovery details. A failed recovery remains visibly labeled. Tab close controls are centered red buttons; closing a dirty tab retains the normal save/discard/cancel choice.

The normal footer has two lines: operation/recovery information, then words/lines/caret and build version. Document details opens workspace, path, identity, format and recovery information without crowding the footer. Workspace Files is headed by the workspace name; its tooltip shows the root directory. Persistent operation failures remain visible until resolved or explicitly dismissed.

On Windows, Git's Open terminal here opens Windows PowerShell in the currently inspected repository root. It rechecks the root first. External commands are your responsibility; refresh Git in Axiom after making changes. This is an external terminal, and does not upload source or save unsaved buffers.


## Discovery and embedded Git terminal

Open Discovery using the search icon or Ctrl+Shift+F. It searches **saved Markdown in the active workspace**, showing each matching file, passage and source line. Enter in the query runs a search; Open match, Enter on a result, or double-click opens its source passage. Existing dirty tabs keep their buffers. If the source passage changed, Axiom reports that the old match cannot be selected instead of assuming an offset is current.

Choose **Right panel** or **Bottom results** in Discovery or Settings. The preference survives restart. Switching placement retains the same query, results and selected match. Backlinks, Relations and Classify open the existing active-document views; their **Return to discovery results** button restores the retained search without rerunning it. The palette also offers Return to retained discovery results. Backlinks are saved Markdown references; explicit relationships are deliberately stored links. Offline results do not include unsaved edits.

Discovery shows CP connection state and offers **Reviewed CP search**. Connect through File → CP connection and reviewed insights first. The existing review window lets you select buffers or saved sources, inspect what will be sent, and then explicitly submit. It retains citation validation, source-hash freshness checks and read-only results. CP-derived related-document suggestions do not create relationships. A disconnected/unavailable CP service leaves offline discovery and editing available.

In **Customize Toolbar**, choose **Top commands** or **Side commands** along with the existing visibility and ordering choices. Apply persists them; Reset restores the default top toolbar. The main menu stays in the title bar. Zen and Presentation hide the side toolbar and restore it on exit. These changes do not save documents or replace the editor's Undo history.

On Windows, open Git to use an embedded PowerShell terminal at the displayed repository root. **Terminal** is the default tab; **Changes** keeps the managed saved-Markdown workflow described above. Switching tabs or collapsing/reopening Git retains the shell, output and pending command. Refresh Changes after shell operations.

Buttons below the terminal insert `git status`, `git add .`, `git add -u`, `git diff`, `git diff --cached`, `git commit -m ""`, or `git push`. They never send Enter. Review/edit the prepared command and **press Enter in the terminal to run it**. `git add .` stages tracked changes/deletions and untracked files under the current root; it is not an untracked-only command. **Commit…** inserts `git commit -m ""`, focuses the terminal, and puts the cursor between the quotes: start typing the message, then press Enter. Git hooks and signing configuration still apply. To use the configured Git message editor instead, type `git commit` yourself. Push executes only when you press Enter; normal authentication remains your shell/Git workflow.

Helpers are available only at an empty prompt at the original repository root. They do not replace pending input or inject into running programs. Shell history/cursor editing and completion conservatively disable helpers until an empty prompt returns; Ctrl+C cancels the current command/input. If you change directories, type a command to return to the displayed root before using helpers. **Restart shell** starts a new shell after exit; restarting a running shell asks you to confirm stopping it and discarding its pending command.

The terminal is a real shell: commands can change any accessible saved files, including non-Markdown files, and run Git hooks or remote operations. Unsaved editor buffers remain separate and are never silently saved by a helper. Changing workspaces or closing Axiom stops this shell and its attached programs. Open terminal here remains available in Changes as an external fallback. The frontend and its licenses are bundled for offline use.

## Local CP controls

For advanced/manual connection, open Main menu → File → CP review and search → Advanced connection. On Windows with the configured local CP setup, **Copy local credential** copies the existing credential to the clipboard. Paste it into the password field. **Start local CP** runs the existing startup helper, uses `http://127.0.0.1:5277`, and copies the same credential. It can also be used when that local service is already running. Then explicitly choose **Connect / check capabilities**. Neither helper connects automatically nor sends a document. On Windows, Copy local credential and Start local CP remain available while idle and check their local helpers when clicked; a missing helper shows its expected path. Both buttons are disabled while a request is running; manual endpoint/credential entry remains available for another service.

The local helpers are installed in `%LOCALAPPDATA%\Axiom\CpConnection`. They start the normal CP Development API, including its Development services/data, using installed Ollama models. They do not add automatic Windows startup. The endpoint is accessible only from this computer. Axiom runs only the two fixed helper launchers in that folder; a trusted local setup is required. **Help** in the connection screen opens its bundled topic; F1 does the same. Disconnect clears the Axiom session credential, not the reusable local setup.

## Semantic search: step-by-step test

For everyday use, open CP search, type a query and choose **Search selected documents**. The current document is selected initially; other open or saved documents are included only when you select them. Connection and index preparation/reuse happen within the Search action. You can still choose Build reviewed semantic index separately for diagnostics, but it is not a prerequisite. Clear discards derived results; the next Search prepares what it needs. The detailed walkthrough below is for inspecting behavior and quality, not a required startup process.

Semantic search finds source passages by meaning. It uses embeddings to rank the documents you explicitly select; it does not search every file automatically. Insights answers a question in generated prose. These are separate operations in the same review screen.

### Prepare three small documents

Create a disposable workspace folder and save these three Markdown files. Open all three in Axiom, then activate Solar.md before opening the review screen. No enrollment or metadata is needed for the first test.

**Solar.md**

```markdown
## Solar electricity
Solar panels convert sunlight into electricity.
The system generates power during daylight.
Batteries can store surplus energy for use at night.
```

**Wind.md**

```markdown
## Wind electricity
Wind turbines use moving air to generate electrical power.
Output varies with wind speed.
Batteries can store surplus energy when generation exceeds demand.
```

**Lunch.md**

```markdown
## Lunch recipe
Combine cooked pasta with tomatoes and olive oil.
Add basil and serve warm.
Store leftover food in the refrigerator.
```

### Connect and review the sources

1. For advanced/manual connection, open Main menu → File → CP review and search → Advanced connection. If already connected, retain that session. With the configured local Windows setup, click Start local CP or Copy local credential, paste into the password field, and Connect / check capabilities at `http://127.0.0.1:5277`. Confirm embeddings are configured and catalog models are present. Close the connection screen; closing retains the session connection.
2. Open Main menu → File → CP review and search → Reviewed semantic search and cross-document analysis. **Help** or **F1** in that screen opens this walkthrough.
3. Tick the three document checkboxes. Read the exact SOURCE/HASH/text snapshots below them. Leave the optional Type/Tag filters empty and derived-vector persistence unchecked for the first pass. Nothing has been uploaded merely by selecting documents.
4. If a document is closed, Add saved workspace documents for review can add it from the active workspace. Added sources start unchecked; select them explicitly. Use an open document's reviewed buffer instead of adding another saved copy of it.

### Build and search by meaning

5. Click **Build reviewed semantic index**. This submits only the selected source sections for embeddings. Wait for Index built with provider/model and current hashes. **Semantic search** and **Related to first selected document** should become enabled. A missing model/service or failed build should explain the failure and leave Markdown intact.
6. In the shared query/question field, type **Which passages discuss generating electrical energy from moving air?** Click **Semantic search**. You should see ranked source-section rows showing document reference, heading, line and similarity. Wind.md should be relevant. Solar.md may also rank because it concerns electricity. Lunch.md should be less useful for this query. Exact order and numerical scores are model-dependent; assess whether results help you locate the passage. Similarity is not a percentage confidence or proof of truth.
7. Select the Wind.md result, then **Open selected source section**. The review window closes and Axiom opens/selects that captured source range. Confirm the expected text is selected and the file was not edited or saved by navigation. This also tests useful source navigation rather than merely receiving a numeric score.

### Related documents and cross-document insights

8. Reopen the semantic screen with Solar.md active, select the sources again and rebuild; the default index lives only in that review window. Confirm Solar.md is the first selected source in the displayed order. Click **Related to first selected document**. Its own sections are excluded; Wind.md should be a useful related candidate. This proposes matches and does not create a stored relationship.
9. Replace the question with **Compare how the two energy documents generate electricity and handle surplus power. Cite the reviewed sources and exclude the recipe.** Click **Analyze reviewed documents**. This submits the selected snapshots for generated cross-document insights; it is separate from semantic ranking and does not require building an index first. Review the answer, source manifest and resolved citation rows. Select a resolved citation and Open selected cited source range to check its text. A valid citation location does not prove that the claim is supported; inspect it yourself. Missing/invalid citations stay unverified rather than becoming navigation targets.

### Freshness, cancellation and optional reuse

10. For a freshness check, add a saved closed workspace document, select it and build. While the review screen stays open, edit that saved file in another editor and save it. Axiom should refuse an operation on the stale captured source and ask you to reopen for fresh snapshots. Reopen, review and rebuild. Do not use an old score as evidence about changed text.
11. **Clear derived index/results** clears the session's index, ranked matches and answer without touching source files. Search/Related require another completed build. **Cancel request** requests cancellation; if a short request already completed, its completed result can remain. A cancelled/failed rebuild must not publish a partial index.
12. Optional: tick **Reuse/store derived vectors in local Axiom state**, build, then build the same selection again. With a verified unchanged model digest, the status should report reused sections. This stores derived vectors, source hashes and ranges, not document text or the credential. Changed source/model hashes must prevent stale reuse. **Clear all persisted semantic results** removes that local derived cache without changing Markdown or metadata. Type/Tag fields filter ranked matches using captured local classification; leave them blank until you have deliberately assigned matching metadata.

### Record the result

Pass the workflow when selection/review, completed embedding build, useful semantic matches, source navigation, cross-document review and stale-source refusal work as described. Record the running Axiom build, provider/model, the query and anything confusing. A network success proves connectivity; it does not establish useful rankings or grounded answers. Ordinary Discovery remains literal search over saved workspace Markdown and needs no CP connection.

The semantic screen keeps **Operation status** and its main action buttons below the scrolling review/results area. Check this status after Build, Search, Analyze, Clear or Cancel. It reports source-change refusals, completion and reused sections. A refused request leaves earlier derived results visible; they are not fresh results from that request. An answer with no validated citation ranges is explicitly marked unverified and cannot provide cited-source navigation. Valid citation locations still require you to check whether the source supports each claim.
