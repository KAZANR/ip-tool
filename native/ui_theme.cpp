#include "ui_theme.h"

namespace networkdoctor {

UiTheme lightUiTheme() {
    UiTheme theme;
    theme.tokens = components::theme::light();
    theme.tokens.background = eui::Color("#F4F6F8");
    theme.tokens.surface = eui::Color("#FFFFFF");
    theme.tokens.surfaceHover = eui::Color("#EFF4FB");
    theme.tokens.surfaceActive = eui::Color("#E4ECF8");
    theme.tokens.text = eui::Color("#182230");
    theme.tokens.border = eui::Color("#D9E0E8");
    theme.tokens.primary = eui::Color("#2563EB");
    theme.tokens.metrics.radius.card = 14.0f;
    theme.tokens.metrics.radius.section = 18.0f;
    theme.motion = eui::Transition::make(0.18f, eui::Ease::OutCubic);
    return theme;
}

}
