using Avalonia.Controls;

namespace Axiom.App;

internal sealed record WorkbenchViewSnapshot(WindowState WindowState
                                          , double WorkspaceWidth
                                          , bool WorkspaceVisible
                                          , bool ToolVisible
                                          , bool RailVisible
                                          , bool HelpVisible
                                          , bool TabsVisible
                                          , bool TopBarVisible
                                          , bool ReadingMode
                                          , ColumnDefinitions EditorColumns
                                          , ColumnDefinitions? PreviousEditingColumns
                                          , double HorizontalOffset
                                          , double VerticalOffset
                                          , DocumentTab Tab);
