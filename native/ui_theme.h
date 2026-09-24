#pragma once

#include "eui_neo.h"

namespace networkdoctor {

struct UiTheme {
    components::theme::ThemeColorTokens tokens;
    eui::Transition motion;
};

UiTheme lightUiTheme();

}
