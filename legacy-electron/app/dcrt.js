// A small engine that runs the Craft Library screens (Main.dc.html and Thumb.dc.html)
// inside the desktop app. It reads each screen's layout and logic, and draws it with React.
(function () {
  'use strict';
  var h = React.createElement;
  var registry = {};

  // ----- {{ hole }} lookups -----
  function lookup(path, scopes) {
    path = path.trim();
    if (path === 'true') return true;
    if (path === 'false') return false;
    if (path === 'null') return null;
    if (/^-?\d+(\.\d+)?$/.test(path)) return Number(path);
    if (/^'.*'$|^".*"$/.test(path)) return path.slice(1, -1);
    var parts = path.split('.');
    var cur, found = false;
    for (var i = scopes.length - 1; i >= 0; i--) {
      if (scopes[i] && Object.prototype.hasOwnProperty.call(scopes[i], parts[0])) { cur = scopes[i][parts[0]]; found = true; break; }
    }
    if (!found) return undefined;
    for (var j = 1; j < parts.length; j++) { if (cur == null) return undefined; cur = cur[parts[j]]; }
    return cur;
  }
  var WHOLE = /^\s*\{\{\s*([^}]+?)\s*\}\}\s*$/;
  var HOLE = /\{\{\s*([^}]+?)\s*\}\}/g;
  // Each piece of text is split into fixed parts and {{ holes }} once, then reused on every redraw.
  var compiled = new Map();
  function compile(str) {
    var c = compiled.get(str);
    if (c) return c;
    var w = WHOLE.exec(str);
    if (w) c = { whole: w[1].trim() };
    else {
      var parts = [], last = 0, m;
      HOLE.lastIndex = 0;
      while ((m = HOLE.exec(str))) { parts.push(str.slice(last, m.index)); parts.push({ p: m[1].trim() }); last = m.index + m[0].length; }
      parts.push(str.slice(last));
      c = { parts: parts };
    }
    compiled.set(str, c);
    return c;
  }
  function interp(str, scopes) {
    var c = compile(str);
    if (c.whole) return lookup(c.whole, scopes);
    var out = '';
    for (var i = 0; i < c.parts.length; i++) {
      var part = c.parts[i];
      if (typeof part === 'string') out += part;
      else { var v = lookup(part.p, scopes); out += v == null ? '' : String(v); }
    }
    return out;
  }

  // ----- styles -----
  function camel(prop) {
    if (prop.indexOf('--') === 0) return prop;
    if (prop.charAt(0) === '-') { prop = prop.slice(1); var c = prop.replace(/-([a-z])/g, function (m, l) { return l.toUpperCase(); }); return c.charAt(0).toUpperCase() + c.slice(1); }
    return prop.replace(/-([a-z])/g, function (m, l) { return l.toUpperCase(); });
  }
  function parseStyle(str) {
    var out = {}, depth = 0, buf = '', parts = [];
    for (var i = 0; i < str.length; i++) {
      var ch = str.charAt(i);
      if (ch === '(') depth++;
      if (ch === ')') depth--;
      if (ch === ';' && depth === 0) { parts.push(buf); buf = ''; } else buf += ch;
    }
    parts.push(buf);
    parts.forEach(function (p) {
      var k = p.indexOf(':');
      if (k < 0) return;
      var name = p.slice(0, k).trim(), val = p.slice(k + 1).trim();
      if (name && val) out[camel(name)] = val;
    });
    return out;
  }

  // ----- attributes -----
  var ATTR = { 'class': 'className', 'for': 'htmlFor', tabindex: 'tabIndex', readonly: 'readOnly', maxlength: 'maxLength', autofocus: 'autoFocus', viewbox: 'viewBox' };
  var EVENTS = { onscroll: 'onScroll', onclick: 'onClick', ondoubleclick: 'onDoubleClick', ondblclick: 'onDoubleClick', onkeydown: 'onKeyDown', onkeyup: 'onKeyUp', onchange: 'onChange', oninput: 'onInput', onfocus: 'onFocus', onblur: 'onBlur', onmouseenter: 'onMouseEnter', onmouseleave: 'onMouseLeave', onpointerdown: 'onPointerDown' };
  var SKIP = { 'data-dc-script': 1 };
  function propsFor(el, scopes, isSvg) {
    var props = {};
    for (var i = 0; i < el.attributes.length; i++) {
      var a = el.attributes[i];
      var name = a.name, raw = a.value;
      if (SKIP[name] || name.indexOf('hint-') === 0) continue;
      var val = raw.indexOf('{{') >= 0 ? interp(raw, scopes) : raw;
      var lname = name.toLowerCase();
      if (EVENTS[lname]) { if (typeof val === 'function') props[EVENTS[lname]] = val; continue; }
      if (lname === 'ref') { if (typeof val === 'function') props.ref = val; continue; }
      if (lname === 'style') { props.style = parseStyle(val == null ? '' : String(val)); continue; }
      if (lname === 'disabled' || lname === 'checked') { props[lname] = val === '' ? true : !!val && val !== 'false'; continue; }
      if (lname === 'value') { props.value = val == null ? '' : val; continue; }
      var key = ATTR[lname] || name;
      if (isSvg && key.indexOf('-') > 0 && key.indexOf('aria-') !== 0 && key.indexOf('data-') !== 0) key = camel(key);
      props[key] = val == null ? '' : val;
    }
    if (props.value !== undefined && props.onChange === undefined && (el.localName === 'input' || el.localName === 'textarea' || el.localName === 'x-select')) props.readOnly = true;
    return props;
  }

  // ----- turning the layout into React elements -----
  function renderNodes(nodes, scopes, prefix) {
    var out = [];
    for (var i = 0; i < nodes.length; i++) {
      var r = renderNode(nodes[i], scopes, prefix + '.' + i);
      if (r == null) continue;
      if (Array.isArray(r)) out.push.apply(out, r); else out.push(r);
    }
    return out;
  }

  function renderNode(node, scopes, key) {
    if (node.nodeType === 3) {
      var t = node.nodeValue;
      if (/^\s*$/.test(t) && t.indexOf('\n') >= 0) return null;
      if (t.indexOf('{{') >= 0) { var v = interp(t, scopes); return v == null ? '' : String(v); }
      return t;
    }
    if (node.nodeType !== 1) return null;
    var tag = node.localName;
    if (tag === 'helmet' || tag === 'script') return null;
    if (tag === 'x-dc') return renderNodes(node.childNodes, scopes, key);
    if (tag === 'sc-if') {
      var cond = interp(node.getAttribute('value') || '', scopes);
      return cond ? h(React.Fragment, { key: key }, renderNodes(node.childNodes, scopes, key)) : null;
    }
    if (tag === 'sc-for') {
      var list = interp(node.getAttribute('list') || '', scopes) || [];
      var as = node.getAttribute('as') || 'item';
      return list.map(function (item, idx) {
        var scope = {}; scope[as] = item; scope.$index = idx;
        return h(React.Fragment, { key: key + ':' + idx }, renderNodes(node.childNodes, scopes.concat([scope]), key + ':' + idx));
      });
    }
    if (tag === 'dc-import') {
      var Child = registry[node.getAttribute('name')];
      if (!Child) return null;
      var cp = { key: key };
      for (var i = 0; i < node.attributes.length; i++) {
        var a = node.attributes[i];
        if (a.name === 'name' || a.name.indexOf('hint-') === 0) continue;
        cp[camel(a.name)] = a.value.indexOf('{{') >= 0 ? interp(a.value, scopes) : a.value;
      }
      return h(Child, cp);
    }
    var isSvg = node.namespaceURI === 'http://www.w3.org/2000/svg';
    var realTag = tag === 'x-select' ? 'select' : (tag === 'x-option' ? 'option' : (isSvg ? node.tagName : tag));
    var props = propsFor(node, scopes, isSvg);
    props.key = key;
    if (realTag === 'textarea' || realTag === 'input') return h(realTag, props);
    var kids = renderNodes(node.childNodes, scopes, key);
    return kids.length ? h.apply(null, [realTag, props].concat(kids)) : h(realTag, props);
  }

  // ----- base class the screens' logic extends -----
  class DCLogic extends React.Component {
    constructor(props) { super(props); this.state = this.state || {}; }
  }

  function sameProps(a, b) {
    var ka = Object.keys(a), kb = Object.keys(b);
    if (ka.length !== kb.length) return false;
    for (var i = 0; i < ka.length; i++) {
      var k = ka[i], x = a[k], y = b[k];
      if (x === y) continue;
      if (!x || !y || typeof x !== 'object' || typeof y !== 'object' || Array.isArray(x)) return false;
      var kx = Object.keys(x), ky = Object.keys(y);
      if (kx.length !== ky.length) return false;
      for (var j = 0; j < kx.length; j++) if (x[kx[j]] !== y[kx[j]]) return false;
    }
    return true;
  }
  var injected = {};
  function injectHelmet(name, helmet) {
    if (!helmet || injected[name]) return;
    injected[name] = true;
    Array.prototype.forEach.call(helmet.children, function (el) {
      if (el.localName === 'style') {
        var st = document.createElement('style');
        st.setAttribute('data-screen', name);
        st.textContent = el.textContent;
        document.head.appendChild(st);
      }
      // Online font links are skipped: the fonts are stored in the app (fonts.css).
    });
  }

  function define(name, source) {
    var src = source
      .replace(/<select(\s|>)/g, '<x-select$1').replace(/<\/select>/g, '</x-select>')
      .replace(/<option(\s|>)/g, '<x-option$1').replace(/<\/option>/g, '</x-option>');
    var doc = new DOMParser().parseFromString(src, 'text/html');
    var xdc = doc.querySelector('x-dc');
    var script = doc.querySelector('script[data-dc-script]');
    var helmet = xdc.querySelector('helmet');
    var Logic = new Function('DCLogic', 'React', script.textContent + '\nreturn Component;')(DCLogic, React);
    class Screen extends Logic {
      // Parts like thumbnails only redraw when what they show has changed.
      shouldComponentUpdate(nextProps, nextState) {
        if (nextState !== this.state) return true;
        if (super.shouldComponentUpdate) return super.shouldComponentUpdate(nextProps, nextState);
        return !sameProps(this.props, nextProps);
      }
      render() {
        injectHelmet(name, helmet);
        var vals = this.renderVals ? this.renderVals() : {};
        return h(React.Fragment, null, renderNodes(xdc.childNodes, [vals], name));
      }
    }
    Screen.displayName = name;
    registry[name] = Screen;
    return Screen;
  }

  window.DCRuntime = {
    start: function (sources, mainName, mount) {
      Object.keys(sources).forEach(function (n) { define(n, sources[n]); });
      ReactDOM.createRoot(mount).render(h(registry[mainName]));
    }
  };
})();
