<!-- Generated from the maintained Axiom v1 User Guide. Source SHA-256: D408690F244E13A55FC022E2839A8BA4C3B66E5CB8FE99264D2B384F59AC631B. Edit the maintained guide and run scripts/Sync-OfflineHelp.ps1. -->

# Axiom v1 User Guide

## 1.0.3 candidate: manage documents and collections

The File menu adds named workspace document/folder creation, Rename, Move, Duplicate, Delete to Recycle Bin, Remembered Workspaces, and Make Axiom Documents. Select an explorer document, then choose a command. Destinations are workspace-relative and must not already exist. Moves retain identity and warn about relative references; enrolled duplicates get a new ID.

Remembered Workspaces opens another collection or forgets its saved record/pins without deleting files. Bulk enrollment starts with nothing selected; review paths/status, select eligible files, and Apply to explicitly save their IDs. Cancellation stops remaining work and retains completed files; inspect per-file results before retrying.

Press/release **Alt** or press **F10** to focus File, then Space/Enter to open its menu. **Alt+F**, **Alt+E**, and **Alt+M** open File, Edit, and Format directly. Move opens a folder picker: choose an existing folder inside this workspace; Axiom retains the filename and confirms the exact destination and reference warning.

The explorer selects one document. For bulk enrollment, choose **Enroll multiple files…** beside it or **File → Bulk Make Axiom Documents…**. Select multiple rows in that separate review window by clicking each row or pressing Space on a focused row; Shift selects a range. Watch the selected count, then explicitly Apply. Opening the window or selecting rows does not enroll files.

Files restored or added externally appear after the workspace watcher refreshes the explorer; your dirty editor stays intact. **F5** or **File → Refresh Workspace** requests a rescan. External restore does not recreate removed pins automatically; Axiom deleted-document recovery restores its recorded pin.

Use each candidate's `Test-Axiom.cmd` and disposable collections for independent testing. Native workflow acceptance remains pending. See [Document and Workspace Management](../04%20-%20Features/Document%20and%20Workspace%20Management.md).

## Open and edit

In accepted 1.0.4 and later, startup restores the active remembered workspace and the explorer includes empty folders. Missing roots remain remembered for recovery. The 1.0.1 behavior described below applies to that older accepted version.

1. Open **File…**, select **Open Workspace…**, and choose a document folder. Axiom adopts the folder as its active workspace and remembers workspace-owned state locally.
2. Expand folders in the **Files** tab and double-click a `.md` or `.markdown` document, or choose **File… > New**. Folders appear before documents at each level.
3. Edit source on the left and read the live preview on the right.
4. Use **File… > Save** (`Ctrl+S`) or **File… > Save As…** (`Ctrl+Shift+S`) when the Markdown file should change. Undo and Redo are under **Edit…**.

While you edit the current document, Axiom keeps the preview near the same proportional reading position after live updates. Opening or reloading another document starts that document at its own preview position. Wide tables scroll inside the preview, and large images scale down to the available width.

Use **Format…** to apply bold, italic, inline code, links, paragraph or heading styles, bulleted, numbered or task lists, and fenced code blocks to the current selection or caret. `Ctrl+B`, `Ctrl+I`, and `Ctrl+K` apply bold, italic, and link formatting while the source editor has focus. Each formatting action is one undo step and remains unsaved until you choose **Save**.

At the end of a non-empty list item, press **Enter** to continue its bullet, next visible number, or an unchecked task box. Press **Enter** on an empty list item to remove its marker and end the list. Press **Tab** or **Shift+Tab** to indent or outdent the current list item or selected list lines by one level. Outside list content, Tab keeps the editor's normal behavior.

To reference an existing image, first save the current Markdown document inside the active workspace, then choose **Image…**. If no workspace is open when a new document is first saved, Axiom adopts the containing folder as the workspace and refreshes Files. The image picker starts in that workspace. Select a PNG, JPEG, GIF, WebP, BMP, or SVG file that is also inside it. Axiom inserts a portable relative Markdown path; selected single-line text becomes the alt text, or a filename-derived placeholder is selected for you to replace. Preview resolves supported workspace-local images without rewriting that portable source reference. Saving the Markdown remains explicit. Axiom does not copy or manage the image file. If a selection is outside the workspace or otherwise unusable, Axiom leaves the Markdown unchanged and explains why.

Choose **Theme: System**, **Theme: Light**, or **Theme: Dark** from the command bar to change the application and preview appearance. System follows the supported operating-system setting. Axiom remembers the choice across launches. Switching themes does not change the Markdown buffer, selection, caret, workspace, or undo history.

Opening a folder as a workspace does not copy, rewrite, save, or enroll any Markdown file. Selecting the same folder again reuses its Axiom workspace record. Axiom 1.0.1 does not yet reopen that workspace automatically at startup or provide a saved-workspace switcher.

The explorer shows only folders that contain Markdown and the `.md` or `.markdown` documents beneath them. Non-Markdown files are omitted. Axiom does not follow linked directories; inaccessible or linked descendants are skipped with a warning while the rest of the workspace remains usable.

## Search the workspace

1. Select the **Search** tab in the left workspace pane.
2. Enter literal text and choose **Search**.
3. Review matches by relative file path, excerpt, and line number.
4. Double-click a result to open the document and select the matching text.

Search is case-insensitive and reads the current `.md` and `.markdown` files directly. It does not replace text, modify files, use regular expressions, or maintain a content index. Starting another search supersedes the prior one. If a result changed after the search, Axiom opens the document but does not select an incorrect location.

## Navigate by document outline

Select the **Outline** tab in the left workspace pane to see headings from the current editor buffer. The outline updates shortly after typing, including for unsaved changes. It supports normal `#` headings and setext headings, ignores heading-like text inside fenced code, and keeps duplicate heading names as separate entries.

Double-click an outline entry to move the editor caret and viewport to that heading. Outline navigation does not save or modify the document.

On the `benny/axiom-1.0.5` development branch, outline and preview share the same heading interpretation. Preview headings have GitHub-style fragment IDs, including distinct suffixes for repeated headings. Document tabs and clean-session restoration are available as described below; link-following commands and a backlinks panel remain under development.

## Identify the running build

The right side of the bottom status bar shows the running Axiom product version and build provenance. Release builds use the publishing commit ID; local development builds explicitly show `dev`. This makes it possible to distinguish the executable being tested without opening or changing a document.

## Pin important documents

Open a Markdown document inside the active workspace and choose **Pin** in the toolbar. The document appears in the **Pinned** list above the folder tree. Double-click a pin to open it through the normal unsaved-change protection. Choose **Unpin** while the document is active, or select a pin and choose **Remove**.

Pins are workspace-specific Axiom state. Pinning ordinary Markdown does not enroll or modify it. If a pinned file is moved or deleted outside Axiom, its pin remains visible and removable; opening it reports that the target is missing.

## Read the status areas

The message beside the toolbar reports the latest operation or error. The bottom status area remains stable and shows:

- the current document or workspace path;
- the active workspace name;
- whether the document is ordinary Markdown or an Axiom document;
- whether it is new, saved, unsaved, or conflicted;
- recovery protection or failure state; and
- the running version and build provenance.

Because transient messages and durable context are separate, a save confirmation or error does not hide the current document state.

An asterisk in the window title means the editor differs from the saved file. Closing or leaving dirty work offers Save, Discard, or Cancel.

## Axiom identity

Existing files do not become Axiom documents automatically. Select **Make Axiom Document** only when stable identity is useful. The new ID is an unsaved editor change until you save. If copied files contain the same ID, Axiom warns and offers a distinct ID for the copy.

## Structured Markdown and wiki navigation (1.0.14 candidate)

**File → Document Structure…**, also available as **Browse document structure** in the palette, shows headings, sections, tasks, links, fenced/indented code and tables from the current buffer. Select a row and **Show source** to select its exact source range. Browsing never edits or saves Markdown. Logical node IDs are reconciled across ordinary edits; a changed or ambiguous node requires refreshing the browser. A section runs from its heading to the next heading of the same or higher level.

Optional wiki syntax supports `[[Document Name]]`, `[[Document Name|label]]`, `[[Document Name#Heading text|label]]`, `[[folder/document.md#heading-anchor]]`, and `[[id:document-guid]]`. A heading-only `[[#Heading text]]` uses the current document. Explicit paths are relative to the workspace root; names match filename stems, first heading titles and saved Axiom aliases. Name matching ignores case; duplicate matches require choosing a target. An exact anchor wins; otherwise unique heading text is matched without case sensitivity. Repeated heading text requires an exact anchor. Escape a literal pipe, closing bracket or backslash inside a wiki target/label with a backslash. Backtick/fenced code examples and escaped opening brackets remain literal.

Follow a wiki link from Preview or place the editor caret inside it and use **Ctrl+Enter / Follow Markdown Link**. The same navigation history and dirty-buffer protections apply. Wiki links appear in backlinks, including unsaved source overlays. Saved alias changes schedule an index refresh. While an index is incomplete, name/identity uniqueness remains unverified. Missing targets never create or enroll documents; resolve the missing target deliberately. Absolute paths, traversal, remote schemes and inaccessible/reparse targets are rejected. Wiki navigation preserves current unsaved target headings without saving either buffer.

The semantic foundation exposes structural snapshots and stores derived results in local state with document/content-hash/revision and provider/version provenance. It does not generate AI results in this version. Derived cache data never becomes Markdown or classification authority; later semantic features must verify matching provenance before using it.

## Recovery

### Deleted documents and recorded folder structure (1.0.13 candidate)

**Delete Document to Recycle Bin** captures exact saved bytes, original workspace path, document identity, pin and a classification/relationship receipt before recycling. Capture failure prevents deletion. Protected recycle supports Markdown documents up to 20 MiB; larger files remain untouched. Resolve unsaved changes first: recovery captures the saved file, not discarded edits.

Choose **File → Recover Deleted Documents…** or **Recover deleted documents** in the command palette. Select a receipt to preview its captured source and status, then **Restore selected…**. Choose a workspace-relative Markdown path and confirm **Preserve identity** or **New identity**. Cancel keeps the capture and disk unchanged. Missing parent folders are recreated; recovery never overwrites an existing file. Choose another path when the original path is occupied. A preserved identity already present elsewhere requires resolving that conflict or explicitly choosing a new identity.

Preserving identity reconnects classifications and relationships still retained in current state. Newer metadata remains authoritative; the historical receipt never replaces it. A new identity does not inherit the original metadata or redirect original relationships. Missing relationship endpoints remain unresolved. The recorded pin is restored to the selected path.

Captures live in local Axiom state, separately from Windows Recycle Bin and Git history. They remain without automatic expiration; protect and back up that state if you need the captures. Recovery covers Axiom-observed document recycling and recorded parent paths, not arbitrary external deletion or recursive folder deletion. A corrupt receipt cannot be restored. Prepared/restoring receipts require restart reconciliation; cancelled/restored receipts remain visible and cannot be restored again. Parent folders created before an interrupted restore may remain empty; retry checks them safely.

Dirty text is protected locally without being written to the Markdown file. After a crash or forced reboot, Axiom can restore that text into the editor for review. Recovery never silently saves it.

If Undo returns the editor exactly to the saved file, Axiom clears the current session's obsolete recovery draft and returns the recovery status to **Recovery ready**.

## External changes

If another editor changes a clean open file, Axiom reloads it. If Axiom also has unsaved edits, a banner opens a side-by-side comparison. Choose **Use my edits**, **Reload disk version**, or **Decide later**. Axiom blocks Save while this conflict remains unresolved.


## 1.0.4: diagnostics, backups, and versions

Open Tools → About Axiom to identify the running build and its state location. Tools → Diagnostics provides Refresh and Copy for a bounded log tail. Review copied diagnostics before sharing because paths and exception details may appear.

Tools → State Maintenance shows database integrity/schema and record counts. Export verified backup chooses a new file and refuses an existing destination. Inspection and backup leave Markdown unchanged. Restore requires closing all Axiom processes and following the operational recovery procedure; live restore/reset is unavailable.

Tools → Install Portable Update first explains the operation, then asks you to select **payload-manifest.json** inside a newer published Axiom application package, beside **Axiom.App.exe**. Select the application package, rather than the Markdown workspace; extract ZIP downloads first. Axiom verifies it, displays its identity, and offers explicit installation into a new version folder. Restart is the button in the **Version ready** dialog, not a separate Tools command. It protects unsaved edits with Save/Discard/Cancel and carries forward the current state root. Later leaves this app running and displays the installed package directory. When ready, Tools → Switch Portable Version selects that directory's manifest and offers Restart. Switching also supports an older original package if it can read the current database schema. Keep old packages for rollback; do not replace running binaries manually.

The retained 1.0.3 and 1.0.4 Test-Axiom.cmd launchers use separate Axiom-Test-1.0.3 and Axiom-Test-1.0.4 state roots under LOCALAPPDATA. They still edit whichever Markdown folder you select: use disposable collections. Ben accepted the combined 1.0.4 release, including the 1.0.3 workflows, on September 28; see the [combined retest guide and supplied packages](../03%20-%20Architecture/Testing/Axiom%201.0.4%20Combined%20Acceptance%20Review%20-%202026-09-28.md). The 1.0.4.1 package is an update-test fixture only; start with Candidate's launcher and switch from inside Axiom to test retention of the same state.

## 1.0.5 development: work with document tabs

Opening a workspace file, pin, or search result keeps your other documents open. Selecting its tab preserves each document's edits, undo history, caret/selection and editor position. Opening the same file again selects its existing tab. Tabs with the same filename show relative paths; untitled documents have distinct numbers. An asterisk marks unsaved edits, and Conflict or Missing labels identify inactive files needing attention.

Use Ctrl+Tab / Ctrl+Shift+Tab to cycle tabs. Choose File → Close Tab, press Ctrl+W, or use the tab's × button to close it. Save/Discard/Cancel applies to that tab. Workspace switching, application close and version restart resolve all affected dirty tabs; cancellation or failed Save keeps the transition from proceeding. Save As refuses another tab's already-open path.

Recovery protects saved and untitled dirty tabs independently. Startup offers each protected draft; Later preserves that draft for another launch. Restoring a draft for an already-open clean file reuses that tab. No recovery choice silently writes Markdown.

After a normal close, startup restores saved-file tabs in their previous order and selects the active document. Axiom remembers caret/selection and editor/preview positions separately from Markdown. Each workspace remembers its own clean open set. Missing or unavailable files are skipped with feedback. Untitled work is recovered through protected drafts rather than treated as a saved file. Cancel or a failed Save stops a close or workspace/version transition; successful earlier saves remain saved and other buffers remain open. Native preview position/dialog testing and the 1.0.5 acceptance gate remain pending; Markdown link navigation and backlinks are still under development.

### Follow Markdown links in 1.0.5 development

Click a standard Markdown link in Preview, or place the editor caret inside an inline/reference link and use File → Follow Markdown Link or Ctrl+Enter. Axiom resolves same-workspace .md/.markdown targets, reuses an open tab and positions the editor at the requested heading. Open-buffer headings are used when that target has unsaved edits. Unsupported/external/outside-workspace links show feedback and do not launch content or switch workspaces. A missing heading offers Open document or Cancel; a missing document leaves the current tab selected.

Back/Forward buttons and Alt+Left/Alt+Right return to source caret locations while retaining dirty buffers and undo. Unavailable history locations are skipped with feedback; changing workspaces clears history. A changed preview link requires a refreshed preview. These actions never save or enroll a document. Native preview-click/fragment-scroll usability remains pending human acceptance.

### Incoming Markdown references in 1.0.5 development

Select Backlinks in the workspace sidebar to inspect references to the active saved document. Results show source path, heading/text context, line and Saved or Unsaved provenance. Double-click a result or select it and press Enter to open the reference itself. A changed source is relocated only when the matching destination is unique; ambiguous or removed references require a rebuild.

Rebuild reads saved Markdown off the UI thread; Cancel retains previous results with a Stale indication. Complete means eligible files were processed, while Partial/Error lists skipped or failed paths. Failed source reads retain earlier references under the partial warning; deleted sources are removed on rebuild. Saved file/folder changes schedule a refreshed index. Dirty source tabs replace their saved results with an Unsaved overlay, including an empty overlay when links are removed; Discard restores saved results and Save schedules a saved-file rebuild. Untitled buffers cannot supply relative-path references. Unverified overlay targets are checked when followed; open-target headings are checked against their live buffer. Indexing and navigation never enroll ordinary Markdown.

## Document metadata and classification (increment B candidate)

Open the **Metadata** sidebar tab to inspect the active document's ID, workspace path, and saved identity status. Tags and aliases are entered one per line; Type is optional free text. **Apply** saves these values locally without saving Markdown. **Cancel** discards the panel edits. Leaving the document with pending panel edits offers Apply, Discard, or Cancel. Metadata editing requires a saved unique canonical document ID; use **Make Axiom Document** and deliberate Save for ordinary files, then Refresh saved identities. Untitled, missing, ambiguous, and unsaved identity changes remain read-only.

If the fields are greyed out, read the explanation above them. For an ordinary document, open it from an active workspace, click **Make Axiom document** in Metadata, then **Save document**. Enrollment first adds an unsaved ID marker to the editor; Save deliberately writes it to Markdown. **Refresh saved identities** checks the saved file again. Copied IDs and incomplete discovery show their own reasons and require resolution before editing. Merely opening Metadata never enrolls or saves anything.

For a first example, enter `Project` and `Decision` on separate Tags lines, `Note` as Type, and `Launch plan` as an Alias, then click **Apply**. Tags group documents, Type describes one document, and Aliases are alternate names for finding it; they do not rename files or change links. Open **Classify**, enter `Project` in Tags or `Launch plan` in Filename or alias, and click **Filter**. All entered tags must match; Type matches exactly, and filename/alias uses a literal substring. **Clear** removes the filters. These values are local Axiom metadata; explicit export/import transfers them to another state.

The **Classify** tab filters by every entered tag (one per line), optional exact type, and filename or alias text. Matching is case-insensitive; aliases are alternate names, not filenames or Markdown-link targets. Clear removes filters. Refresh rereads saved identities and reports partial discovery. Double click or Enter on a result opens its existing tab.

**Export metadata** reviews unique saved identities and writes a selected JSON file. **Import metadata** previews every record. All changes default to Skip; select each replacement you want and Apply selected replacements. Missing/ambiguous identities cannot be selected. Files or metadata changed since review require a refreshed preview. Imports replace selected tags/type/aliases atomically, never save/enroll Markdown, and never erase records absent from the transfer file. Local metadata survives rename, missing/recycled documents and forgotten workspaces; a duplicated document with a new ID begins empty. Keep an explicit JSON export if you need these local values in another Axiom state.

Increment B uses state schema 6. Its isolated launcher must use a different state folder from increment A/schema 5. Retain the verified pre-migration backup and old binaries; an older package rejects newer state rather than downgrading it.

## Explicit relationships (increment C candidate)

Open the **Relations** sidebar tab for incoming and outgoing connections for the current saved Axiom document. **Add** creates a directed connection from this document to another unique saved Axiom document in the workspace. Choose a target, keep or change the neutral **Related** kind, enter an optional note, and Apply. Self connections and duplicate direction/kind pairs are rejected. Reverse connections are independent.

Select an outgoing relationship to **Edit** or **Remove** it from its source document. The editor makes an explicit proposal; Cancel leaves it unchanged. Removal confirms your intent. Double click or Enter follows the other endpoint in its existing tab and retains dirty buffers; Back/Forward return to prior locations. A missing or ambiguous endpoint stays visible by ID and does not guess a path. Refresh identities after a file returns or a copied ID is resolved. Relations remain local metadata and do not modify Markdown links or source text.

Metadata export now writes version 2 with classifications and relationships. Unresolved relationship IDs remain in the export when one endpoint belongs to the active unique collection. Import still accepts version 1 classifications. Its review defaults to Skip for every classification/relationship; select each intended replacement. Missing/ambiguous relationship endpoints cannot be applied, and stale files or revisions require refresh. A selected mixed batch commits atomically; failure leaves every value unchanged. Keep the explicit JSON export for transfer to another Axiom state.

Increment C uses schema 7 and a separate `Axiom-Test-1.0.5-C` launcher state. Retain earlier packages and verified pre-migration backups for rollback; do not point older binaries at migrated state expecting an automatic downgrade. Human acceptance of these candidates remains pending.

## Workspace loading, reading mode, and Help (1.0.6 candidate)

**Open Workspace** selects a physical folder containing Markdown. A loading indicator appears while discovery, identity inspection, and metadata discovery run in the background. **Cancel loading** stops discovery and retains the previous workspace and every open buffer. After discovery succeeds, existing Save/Discard/Cancel and pending-metadata safeguards resolve work before adoption. Failures retain the prior workspace. Linked or inaccessible paths are reported separately; partial metadata discovery disables identity-bound changes. Remembered workspaces remain available through File commands.

Click a Markdown leaf once in **Files** to open/reuse its tab. Folders still select/expand; right click and keyboard navigation remain separate. Enter opens a selected leaf. Double clicking reuses the same tab. Use **Enroll multiple files** for the separate bulk review workflow.

Click **Read** or press **Ctrl+Shift+R** to hide the editor and let Preview fill the document area. **Show editor** or the same shortcut restores editing. Tabs, navigation, metadata, conflict banners and dirty indicators remain visible. Toggling does not save, discard, alter identity, or reset undo/caret/selection/scroll; hidden buffers remain recoverable. The preference persists locally, including after clean startup. Recovered unsaved tabs retain their dirty indicators in reading mode.

Click **Help** or press **F1** for a bundled offline copy of this maintained User Guide. The topic selector jumps among headings; context actions from the sidebar suggest relevant workspace, metadata/classification, relationships or navigation guidance. Help does not require enrollment or a network connection and does not modify documents. The bundled copy is generated by `scripts/Sync-OfflineHelp.ps1` from this guide and carries a source SHA-256; edit this guide, then regenerate it. Links to separate project documentation stay within the offline Help navigation restrictions. The 1.0.7 development features below extend Help; menu/icon integration still requires design selection.

With **Relations** selected, F1 opens **Explicit relationships** even when focus is on its header, a button or an empty list. With **Metadata** or **Classify** selected, F1 opens **Document metadata and classification**. The Help topic selector shows the selected heading so you can confirm the context or choose another topic.

## Export to Word and HTML (1.0.7 development)

Use **File → Export current buffer to Word** or **Export current buffer to HTML**. Export uses the active document as it appears now, including unsaved edits. It does not save the Markdown, change enrollment, or discard your work. Choose an output filename ending in `.docx` or `.html`; replacement requires confirmation. Cancel export during conversion/writing to retain any existing output.

Both formats support headings, paragraphs, emphasis, lists, static task checkboxes, links, tables, quotes and code. HTML is a standalone offline file; Word export requires no Word installation. Supported PNG/JPEG/GIF/BMP images are embedded relative to a saved document path, up to 20 MiB each. Missing/unsupported images or images in untitled documents retain alt text and produce warnings. Remote images are not downloaded. Raw HTML is displayed as text; unsafe links are omitted. Axiom identity markers and separate classification/relationships are not exported. Links to Markdown targets are not recursively exported or rewritten. Word page layout may differ from Preview and requires human review; export is not a lossless import/export editing workflow.

Only the active document is exported. Metadata JSON transfer remains a separate workflow. Export completion shows the destination; errors and warnings explain incomplete conversion. Use **Cancel export** to stop a pending write; existing output is retained until successful commit. Choose an output location outside Axiom state and open/source documents.

## Searchable Help while working (1.0.7 development)

**Help** opens a side panel in the main window. Keep it open while editing, selecting tabs, reading or using metadata and relations. Search matches literal text in headings and the body, without regard to case. Select a matching passage to jump to its topic and highlight matching text where it appears in the rendered guidance. Empty and no-match searches report their state. Browse topics at any time; ordinary focus changes do not retarget a manually chosen topic. **F1** or explicit contextual Help chooses the relevant topic. **Close Help** hides the panel; reopening retains its topic. Use **−/+** to narrow/widen it, or close it to recover the full document area.

Help is generated from this maintained User Guide and stays offline. Product changes must update this guide and regenerate bundled Help; builds and portable publishing check for stale Help when the maintained source is available. Native keyboard focus, themes, DPI and narrow-window usability remain acceptance checks for this development build.

## Settings and editor preferences (1.0.7 development)

Choose **Settings** to configure font family, font size (8–40), wrapping, tab width (1–8 columns) and Light/Dark/System appearance. **Apply** persists the choices; **Cancel** preserves earlier settings. **Reset editor defaults** prepares the original editor choices in the dialog; press Apply to save them. Unknown fonts fall back to Consolas/monospace with status feedback. Settings affect every editor tab and survive restart without changing source text, unsaved work, selection, undo or recovery. Theme uses the existing application appearance preference. Shortcut configuration is later work.

## Recent documents (1.0.7 development)

Use **File → Recent Documents** to reopen one of the latest 20 successfully opened documents, with its path shown. Open reuses an existing tab and uses the existing safe workspace switch if needed. Missing files or workspaces give actionable feedback. Cancelled/failed opens do not enter history. **Remove selected** and **Clear history** forget only history; they never delete files, pins, metadata or protected drafts. Remembered Workspaces remains the single workspace registry.

### Approved icon and selectable menus — September 30, 2026

Ben selected the teal architectural A icon and both menu looks. The application window and Windows executable use the A icon. In Settings, choose Menu style: Quiet (flat groups) or Rounded (rounded groups), then Apply. The choice persists across restarts in the existing editor preferences setting; older preferences default to Quiet. Cancel leaves the selection unchanged. Reset editor defaults retains the menu choice. Both styles preserve File/Edit/Format commands, shortcuts and working buffers. Native visual review at different themes and display scales remains a human acceptance check.

## Plain-text export (1.0.8 development)

Use File → Export current buffer to plain text to write a UTF-8 `.txt` file from the active buffer, including unsaved edits. The same overwrite confirmation, cancellation and protected-destination rules apply as for Word and HTML. Markdown files and recovery state are unchanged. Headings and paragraphs become readable text; lists retain nesting and numbering, tasks retain checkbox symbols, quotes retain a `>` prefix, and code retains indentation. Links include their destination when different from the label. Images retain alt text without embedded bytes. Tables use ` | ` separators and warn that column alignment is approximate. PDF export is available in this development candidate. Human acceptance remains pending.

## PDF export (1.0.8 development)

Use File → Export current buffer to PDF, choose Letter or A4 portrait, and select a `.pdf` output file. Export captures the active buffer when invoked, including unsaved edits, and writes directly without a printer dialog, Office or an online conversion service. Output uses neutral light styling, 18 mm margins and page numbers. Headings, emphasis, nested lists/tasks, quotes, code, safe hyperlinks, tables and supported local images are retained. Table headers repeat; very tall rows can continue across pages and produce a review warning. Long code and URLs wrap; images fit printable width and height. Missing/unsupported images retain alt text and warnings. No remote images or executable HTML are loaded. Unicode is searchable/selectable using installed Windows fonts and browser fallback; unusual scripts require a suitable installed font.

The Windows export renderer requires the same WebView2 Runtime as Preview. Export is limited to 4 million source characters, 64 million HTML characters including embedded images, 20 MiB per image and 128 MiB output. PDF rendering times out after 60 seconds. Cancel or failure preserves an existing destination and the active document; only successful output commits replace it. Cancellation closes the isolated renderer; it cannot interrupt a synchronous Markdown parse already executing. Keep your Markdown as the editable source. Human review of the exact candidate remains pending until all planned features are implemented.

## Templates and named creation (1.0.9 development)

File → New Workspace Document offers Blank or Choose template. Keep reusable `.md` or `.markdown` templates in any local folder you own; the template picker selects a file without changing it. Templates can contain `{{title}}` (the new filename without its extension) and `{{date}}` (local creation date as `yyyy-MM-dd`). Other tokens remain literal. Templates do not run scripts or fetch online content. A canonical first-line Axiom identity is removed from the expanded text, so the template's identity is not copied. New documents remain ordinary Markdown until explicitly enrolled.

Choose an existing folder inside the workspace and enter the Markdown filename. Review all expanded Markdown in the preview before choosing Create document. Cancel closes the workflow without creating a file. Existing filenames, invalid names, paths outside the workspace and linked destinations are rejected. Creation opens a new tab and retains existing dirty tabs. Templates are limited to 4 MiB; relative links and images remain relative to the new document and may need adjustment.

## Drop files and import assets (1.0.9 development)

Drop local Markdown files onto Axiom to open them using the existing tab workflow. Opening keeps the files at their original locations and reuses already-open tabs with unsaved edits. Unsupported files are reported and are not copied. To deliberately copy a Markdown file instead, use File → Copy Markdown into Workspace, choose an existing destination folder and a filename, then confirm the source and destination. Ordinary Markdown copies remain ordinary; an enrolled copy receives a fresh identity. Relative references are not rewritten. Copies use the disk content captured for review, including when a source file is open with unsaved edits.

Use File → Import Image into Workspace, or drop an image, to copy a local PNG, JPEG, GIF, WebP, BMP or SVG into an existing workspace asset folder you select. Save the active document inside the workspace first. Choose a filename retaining the original extension and confirm the copy. Axiom inserts an escaped relative image reference at the caret, using selected single-line text as alt text where possible. Existing image filenames are rejected; choose another name rather than overwrite. Imported bytes are retained exactly and the original stays untouched. Reference insertion is an editor Undo action; undoing it retains the imported image file. If reference insertion fails (for example the caret touches the identity line), the dialog reports the retained image location so you can insert it later. Markdown imports are limited to 4 MiB and images to 20 MiB. Files are processed separately; an unsuccessful file does not roll back earlier successful imports. Copy failures clean up temporary output; a completed copy with a state failure requires restart reconciliation. Human acceptance remains pending.

## Preview navigation and synchronized scrolling (1.0.10 development)

Preview at caret moves the rendered preview to the editor caret's Markdown block. Each rendered top-level block has a Source link that shows the editor, moves the caret to that block's source and focuses the editor. These links are available by keyboard. They use the current rendered document; a stale link after an edit is ignored until Preview refreshes. Markdown links continue to use the existing safe navigation workflow. Document HTML/scripts remain disabled, and synchronization uses application-owned browser code.

Scroll sync starts off on each launch. Toggle it on to synchronize scrolling between editor and preview, using structural blocks rather than whole-document height. Scrolling the preview changes the editor viewport without moving the caret or selection. Scroll sync is suspended in reading mode, during document transitions and while Preview is stale; toggle off whenever you want independent scrolling. Mapping within a large paragraph, table or code block is approximate because source and rendered heights differ. Preview position is retained through edits and tab switches using a unique unchanged block prefix where available, with proportional scroll fallback when the anchor changed or is ambiguous. Navigation, scrolling and reading mode do not save or modify source text.

## Document statistics and operation feedback (1.0.10 development)

The bottom bar shows source-word count and one-based line/column at the caret. Words are contiguous Unicode letter/digit runs with internal apostrophes; code, link destinations and alt text count, while the canonical document identity does not. This is a source count, not a language-aware reading count. Counts refresh after a short typing pause. The format display reports the encoding observed during file reading and the current buffer's CRLF, LF, CR, mixed or absent newlines. Without a BOM, the reader assumes UTF-8; recovered buffers can show encoding unobserved. Save retains the established policy of UTF-8 without BOM. The display does not offer arbitrary encoding conversion.

Success/information messages are nonblocking and expire after eight seconds. A reported operation failure remains visible, with Details and Dismiss controls, even if later informational messages occur. Details displays the full message; dismissing it does not resolve the underlying failure or any existing conflict/reconciliation gate. Dirty/save/recovery state remains in the bottom bar. All human acceptance checks remain deferred until all planned features are implemented.


## Command palette (1.0.11 implementation in progress)

Use File → Command Palette or Ctrl+Shift+P to search the commands currently migrated to shared dispatch. Type a title or command ID, use Down/arrow keys to select a match, and Enter or Run to invoke it. Unavailable commands show a reason and cannot run. Escape or Cancel closes the palette and restores focus. The current checkpoint includes new/save/save-as, undo/redo, reading mode and Help commands. Remaining command migration and native verification are still in progress; this is not a complete 1.0.11 release. The palette displays current configured bindings.

## Keyboard shortcut settings (1.0.11 implementation in progress)

Use File → Keyboard Shortcuts to edit bindings for currently registered commands. Apply validates the whole set and saves it in local application state; changes survive restart. Blank removes an application binding. Cancel discards staged edits. Reset defaults stages the original bindings; press Apply to save the reset. Conflicting or reserved gestures show a validation message and keep the dialog open. The palette displays effective bindings; its Ctrl+Shift+P access remains reserved.

Use Ctrl/Alt/Shift plus a letter, digit or F1–F12. Plain typing keys, Ctrl+Alt (AltGr), standard clipboard/selection, formatting/navigation/menu access, and native editor Undo/Redo are protected. Undo/Redo remain read-only. Unknown retired command IDs are ignored during loading; unsupported or invalid saved configurations fall back to defaults and report an error without overwriting stored data. Save still follows existing confirmation/recovery rules. Only the currently registered command subset is configurable at this checkpoint; comprehensive command migration and native checks remain pending.
## Document history, comparison and restore (1.0.12 candidate)

Git is required only for durable document history. Normal editing, Undo and recovery work without it. Axiom uses local Git history as the revision authority; recovery drafts remain independent. Save never creates a Git commit automatically.

For an existing Git repository, save/open a document and choose File → Document History, or search for Browse document history in the palette. The window lists up to 200 reachable document revisions with commit date, hash, subject and historical path. Git detects committed renames; this does not promise history for uncommitted edits, ambiguous renames or edits made before checkpointing. Select a revision to read its exact Markdown source. Compare it with the captured current buffer, saved buffer, external-change snapshot (when present), or another selected revision. The read-only line viewer shows previous/current line numbers, added/removed/unchanged markers and line-ending differences. Comparison is bounded to 4 Mi characters and 100,000 lines per buffer, and two million comparison cells after shared prefix/suffix trimming. Revisions/checkpoints are limited to 4 MiB. An oversized or unavailable revision reports an error without changing source.

Restore into buffer asks for confirmation, then resolves existing unsaved work with Save, Discard or Cancel. Cancel retains the current buffer and its recovery. External-change conflicts must be resolved first. Axiom preserves the current enrollment identity: a different/missing historical identity is replaced by the current document identity, and an ordinary Markdown document stays ordinary. The restored content is written to recovery before changing the editor; a failed recovery write prevents restore. Restore is undoable and changes only the buffer. Review it and Save explicitly to replace the file; existing conflict/save protections still apply.

If the workspace has no repository, File → Set Up Git History offers explicit local Git initialization. Confirming creates only .git; it does not commit documents or configure a remote. A workspace already covered by Git is not reinitialized. File → Checkpoint Saved Document requires a saved, conflict-free document and reviews its path, repository, byte count and SHA-256 before confirmation. A checkpoint records only those reviewed bytes; changed bytes after review are rejected. It uses a temporary index and a local refs/axiom/document-history reference, leaving the user branch, HEAD, real index and unrelated files untouched. Cancellation creates no visible checkpoint before the final atomic publication; publication finishes once started.

Axiom checkpoint references are local and path-specific. Ordinary Git pushes/clones do not automatically transfer these references. Back up the repository if these revisions matter. Existing committed Git rename history is followed, while a path-specific Axiom checkpoint reference is not automatically moved to another path by a filesystem rename. Workspace Git integration is available in the 1.0.18 candidate; broader history transfer remains external Git work. History source can also be unavailable after deletion or repository changes; choose another readable revision. None of these actions enrolls an ordinary document. Human acceptance and the unfinished 1.0.11 command migration/native checks remain pending.

## Workspace Git (1.0.18 candidate)

File → Workspace Git, or the command palette, shows the local repository, branch and changed paths. Select a document to compare HEAD with its saved file or staged content. The Git view reads saved files; dirty drafts and externally conflicted open documents must be resolved before staging or committing. No operation silently saves a draft.

Stage selected and Unstage selected require explicit confirmation and act only on selected regular Markdown files inside the active workspace. Git's configured line-ending normalization applies to staged content; saved files remain unchanged. Inspect Staged diff to see the bytes that will be committed. Deleted documents can be staged deliberately. Other file types, paths outside the active workspace, coordinated renames/copies, custom/LFS filters, submodules, sparse checkouts, detached HEAD and unresolved Git operations require external Git. Git is optional for editing.

Commit selected staged asks for a message, then reviews the branch, paths and message. It commits only those selected staged entries, using your configured Git author identity. Unrelated staging, working files and dirty buffers remain unchanged. Commits are local, unsigned and run without repository hooks; perform signing or hook-governed workflows in external Git. Axiom never pushes, pulls, changes branches or resets a working tree. Concurrent HEAD/index/source changes reject a stale review. Cancel stops before publication; a commit already published remains committed. Diff/source reads are limited to 4 MiB per document. Human release acceptance remains pending.