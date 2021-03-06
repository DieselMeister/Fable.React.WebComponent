var reactComponentSymbol = Symbol.for("r2wc.reactComponent");
var renderSymbol = Symbol.for("r2wc.reactRender");
var shouldRenderSymbol = Symbol.for("r2wc.shouldRender");

var define = {
	// Creates a getter/setter that re-renders everytime a property is set.
	expando: function (receiver, key, value) {
		Object.defineProperty(receiver, key, {
			enumerable: true,
			get: function () {
				return value;
			},
			set: function (newValue) {
				value = newValue;
				this[renderSymbol]();
			}
		});
		receiver[renderSymbol]();
	}
}


/**
 * Converts a React component into a webcomponent by wrapping it in a Proxy object.
 * @param {ReactComponent}
 * @param {React}
 * @param {ReactDOM}
 * @param {Object} options - Optional parameters
 * @param {String?} options.shadow - Use shadow DOM rather than light DOM.
 */
export default function (ReactComponent, React, ReactDOM, options = {}, embeddStyle = "") {

	var eventHandling = {
		dispatchEvent: {},
		addEventListener: {},
		removeEventListener: {}
	}

	var renderAddedProperties = { isConnected: "isConnected" in HTMLElement.prototype };
	var rendering = false;
	// Create the web component "class"
	var WebComponent = function () {
		var self = Reflect.construct(HTMLElement, arguments, this.constructor);
		if (options.shadow) {
			var sr = self.attachShadow({ mode: 'open' });
		}

		return self;
	};


	// Make the class extend HTMLElement
	var targetPrototype = Object.create(HTMLElement.prototype);
	targetPrototype.constructor = WebComponent;

	// But have that prototype be wrapped in a proxy.
	var proxyPrototype = new Proxy(targetPrototype, {
		has: function (target, key) {
			return key in ReactComponent.propTypes ||
				key in targetPrototype;
		},

		// when any undefined property is set, create a getter/setter that re-renders
		set: function (target, key, value, receiver) {
			if (rendering) {
				renderAddedProperties[key] = true;
			}

			if (typeof key === "symbol" || renderAddedProperties[key] || key in target) {
				return Reflect.set(target, key, value, receiver);
			} else {
				define.expando(receiver, key, value)
			}
			return true;
		},
		// makes sure the property looks writable
		getOwnPropertyDescriptor: function (target, key) {
			var own = Reflect.getOwnPropertyDescriptor(target, key);
			if (own) {
				return own;
			}
			if (key in ReactComponent.propTypes) {
				return { configurable: true, enumerable: true, writable: true, value: undefined };
			}
		}
	});

	WebComponent.prototype = proxyPrototype;

	// Setup lifecycle methods
	targetPrototype.connectedCallback = function () {
		// Once connected, it will keep updating the innerHTML.
		// We could add a render method to allow this as well.
		this[shouldRenderSymbol] = true;
		this[renderSymbol]();
	};
	targetPrototype[renderSymbol] = function () {
		if (this[shouldRenderSymbol] === true) {
			var data = {};
			Object.keys(this).forEach(function (key) {
				if (renderAddedProperties[key] !== false) {
					data[key] = this[key];
				}
			}, this);
			rendering = true;
			// Container is either shadow DOM or light DOM depending on `shadow` option.
			let container = options.shadow ? this.shadowRoot : this;

			// add eventhandling stuff

			let dispatchEvent = function (ev) { container.dispatchEvent(ev) };
			let addEventListener = function (n, f) { container.addEventListener(n, f) };
			let removeEventListener = function (n, f) { container.removeEventListener(n, f) };

			eventHandling.dispatchEvent = dispatchEvent;
			eventHandling.addEventListener = addEventListener;
			eventHandling.removeEventListener = removeEventListener;

			// Use react to render element in container
			this[reactComponentSymbol] = ReactDOM.render(React.createElement(ReactComponent, data), container);

			// adding styleSheets
			if (options.css && options.shadow) {
				if (options.embeddCss) {
					let style = document.createElement('style');
					style.innerHTML = embeddStyle;
					container.appendChild(style);
				} else {
					let style = document.createElement('link');
					style["rel"] = "stylesheet";
					style["type"] = "text/css";
					style["href"] = options.css;
					container.appendChild(style);
				}

			}

			rendering = false;
		}
	};

	// Handle attributes changing
	if (ReactComponent.propTypes) {
		WebComponent.observedAttributes = Object.keys(ReactComponent.propTypes);
		targetPrototype.attributeChangedCallback = function (name, oldValue, newValue) {
			// TODO: handle type conversion
			this[name] = newValue;
		};
	}

	WebComponent.eventHandling = eventHandling;


	return WebComponent;
}
